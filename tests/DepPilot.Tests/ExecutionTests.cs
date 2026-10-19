using DepPilot.Agent;
using DepPilot.Contracts;
using DepPilot.Master.Data;
using DepPilot.Master.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace DepPilot.Tests;

public sealed class ExecutionTests
{
    [Fact]
    public async Task ExpiredLeaseRejectsLateSuccessAndIsNotAutomaticallyRequeued()
    {
        var path = Path.Combine(Path.GetTempPath(), "dep-pilot-lease-" + Guid.NewGuid() + ".db");
        try
        {
            await using var db = new RegistryDb(new DbContextOptionsBuilder<RegistryDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
            await db.Database.EnsureCreatedAsync();
            var clock = new ManualClock();
            var service = new DeploymentService(db, clock, Options.Create(new MasterOptions()));
            var host = new ManagedHost { InstallationId = Guid.NewGuid(), TokenHash = "test" };
            var playbook = new Playbook { Name = "test", Version = "1" };
            db.AddRange(host, playbook); await db.SaveChangesAsync();
            await service.CreateAsync(new("lease", playbook.Id, [host.Id]), "admin", default);
            var job = (await service.ClaimAsync(host.Id, default))!;
            clock.Advance(TimeSpan.FromMinutes(2));
            var ex = await Assert.ThrowsAsync<DomainException>(() => service.CompleteAsync(host.Id, job.JobId, new(job.AttemptId, JobStatus.Succeeded, 0, "late"), default));
            Assert.Equal(409, ex.Status);
            await service.ExpireAsync(default);
            db.ChangeTracker.Clear();
            Assert.Equal(JobStatus.TimedOut, (await db.Jobs.SingleAsync()).Status);
            Assert.Null(await service.ClaimAsync(host.Id, default));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TamperedPlaybookNeverStartsAProcess()
    {
        var path = Path.Combine(Path.GetTempPath(), "dep-pilot-agent-" + Guid.NewGuid());
        try
        {
            var options = new AgentOptions { StateDirectory = path, AnsibleExecutable = "must-not-run" };
            var runner = new PlaybookRunner(options, new AgentState(options));
            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "1", new string('0', 64), 60), "tampered", default));
            Assert.False(Directory.Exists(Path.Combine(path, "jobs")));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task RunnerStartupFailureRemovesTemporaryCredentials()
    {
        var path = Path.Combine(Path.GetTempPath(), "dep-pilot-runner-" + Guid.NewGuid());
        try
        {
            var options = new AgentOptions { StateDirectory = path, AnsibleExecutable = Path.Combine(path, "nonexistent-ansible"), MinioRootPassword = "test-only-private-password" };
            var state = new AgentState(options);
            state.SetHost(Guid.NewGuid());
            var runner = new PlaybookRunner(options, state);
            const string content = "---\n- hosts: localhost\n";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
            var id = Guid.NewGuid();
            await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => runner.RunAsync(new(id, Guid.NewGuid(), Guid.NewGuid(), "test", "1", hash, 60), content, default));
            Assert.False(File.Exists(Path.Combine(path, "jobs", id.ToString("N"), "variables.json")));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void IdentityAndPendingResultsSurviveRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "dep-pilot-state-" + Guid.NewGuid());
        try
        {
            var options = new AgentOptions { StateDirectory = path };
            var state = new AgentState(options);
            state.SetHost(Guid.NewGuid());
            var result = new PendingResult(Guid.NewGuid(), new(Guid.NewGuid(), JobStatus.Succeeded, 0, "ready"), "output");
            state.SaveResult(result);
            var restarted = new AgentState(options);
            Assert.Equal(state.Identity, restarted.Identity);
            Assert.Equal(result, restarted.LoadResult());
            restarted.QuarantineResult();
            Assert.Null(restarted.LoadResult());
            Assert.Single(Directory.GetFiles(path, "rejected-result-*.json"));
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void SecretsAreRedactedIncludingJsonEscapedValues()
    {
        var secret = "test\"secret\\value";
        var escaped = System.Text.Json.JsonSerializer.Serialize(secret)[1..^1];
        var output = OutputSanitizer.Redact($"plain={secret};json={escaped};token=abcdef", secret, "abcdef");
        Assert.DoesNotContain(secret, output);
        Assert.DoesNotContain(escaped, output);
        Assert.DoesNotContain("abcdef", output);
        Assert.Contains("[REDACTED]", output);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }
}
