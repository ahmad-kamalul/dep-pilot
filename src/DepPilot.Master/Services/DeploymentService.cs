using DepPilot.Contracts;
using DepPilot.Master.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DepPilot.Master.Services;

public sealed class DomainException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class DeploymentService(RegistryDb db, TimeProvider clock, IOptions<MasterOptions> options)
{
    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private MasterOptions Settings => options.Value;

    public async Task<Guid> CreateAsync(DeploymentRequest request, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 || request.HostIds is not { Length: > 0 and <= 100 })
            throw new DomainException(400, "Provide a deployment name (1–120 characters) and 1–100 hosts.");
        var hosts = request.HostIds.Distinct().ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Playbooks.AnyAsync(x => x.Id == request.PlaybookId, ct)) throw new DomainException(404, "Playbook not found.");
        if (await db.Hosts.CountAsync(x => hosts.Contains(x.Id) && !x.Revoked, ct) != hosts.Length)
            throw new DomainException(400, "A selected host is missing or revoked.");
        if (await db.Jobs.AnyAsync(x => hosts.Contains(x.HostId) && (x.Status == JobStatus.Pending || x.Status == JobStatus.Running), ct))
            throw new DomainException(409, "A selected host already has an active deployment.");
        var deployment = new Deployment { Name = request.Name.Trim(), PlaybookId = request.PlaybookId, CreatedAt = Now, RequestedBy = actor };
        db.Deployments.Add(deployment);
        db.Jobs.AddRange(hosts.Select(id => new DeploymentJob { DeploymentId = deployment.Id, HostId = id, CreatedAt = Now }));
        Audit(actor, "deployment.created", deployment.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return deployment.Id;
    }

    public async Task<JobAssignment?> ClaimAsync(Guid hostId, CancellationToken ct)
    {
        // SQLite's write transaction serializes claims across requests before reading candidates.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await db.Jobs.AnyAsync(x => x.HostId == hostId && x.Status == JobStatus.Running, ct)) return null;
        var job = await db.Jobs.Where(x => x.HostId == hostId && x.Status == JobStatus.Pending).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (job is null) return null;
        var deployment = await db.Deployments.SingleAsync(x => x.Id == job.DeploymentId, ct);
        var playbook = await db.Playbooks.SingleAsync(x => x.Id == deployment.PlaybookId, ct);
        job.Status = JobStatus.Running;
        job.AttemptId = Guid.NewGuid();
        job.StartedAt = Now;
        job.LeaseExpiresAt = Now + Settings.LeaseSeconds * 1000L;
        Audit($"agent:{hostId}", "job.claimed", job.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(job.Id, job.AttemptId.Value, playbook.Id, playbook.Name, playbook.Version, playbook.Sha256, Settings.JobTimeoutSeconds);
    }

    public async Task<LeaseResponse> RenewAsync(Guid hostId, Guid jobId, Guid attemptId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await OwnedJobAsync(hostId, jobId, attemptId, ct);
        RequireRunning(job);
        job.LeaseExpiresAt = Now + Settings.LeaseSeconds * 1000L;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(job.CancelRequested);
    }

    public async Task CompleteAsync(Guid hostId, Guid jobId, CompletionRequest request, CancellationToken ct)
    {
        if (request.Status is not (JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.TimedOut)
            || request.Summary is null || request.Summary.Length > 2000 || (request.Status == JobStatus.Succeeded && request.ExitCode != 0))
            throw new DomainException(400, "Invalid completion result.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await OwnedJobAsync(hostId, jobId, request.AttemptId, ct);
        if (job.Status == request.Status && job.FinishedAt.HasValue) return; // Retry after lost HTTP response.
        RequireRunning(job);
        job.Status = job.CancelRequested ? JobStatus.Cancelled : request.Status;
        job.ExitCode = request.ExitCode;
        job.Summary = request.Summary;
        job.FinishedAt = Now;
        job.LeaseExpiresAt = null;
        Audit($"agent:{hostId}", "job.completed", job.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task AppendLogAsync(Guid hostId, Guid jobId, LogRequest request, CancellationToken ct)
    {
        if (request.Sequence < 0 || request.Text is null || request.Text.Length > 16000) throw new DomainException(400, "Invalid log batch.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await OwnedJobAsync(hostId, jobId, request.AttemptId, ct);
        RequireRunning(job);
        if (await db.Logs.AnyAsync(x => x.JobId == jobId && x.Sequence == request.Sequence, ct)) return;
        if (await db.Logs.CountAsync(x => x.JobId == jobId, ct) >= 256) throw new DomainException(409, "Log quota reached.");
        db.Logs.Add(new() { JobId = jobId, Sequence = request.Sequence, Text = request.Text, CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(Guid jobId, string actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await db.Jobs.SingleOrDefaultAsync(x => x.Id == jobId, ct) ?? throw new DomainException(404, "Job not found.");
        if (job.Status is not (JobStatus.Pending or JobStatus.Running)) throw new DomainException(409, "Job is already finished.");
        job.CancelRequested = true;
        if (job.Status == JobStatus.Pending) { job.Status = JobStatus.Cancelled; job.FinishedAt = Now; }
        Audit(actor, "job.cancel-requested", job.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RevokeAsync(Guid hostId, string actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct) ?? throw new DomainException(404, "Host not found.");
        host.Revoked = true;
        var jobs = await db.Jobs.Where(x => x.HostId == hostId && (x.Status == JobStatus.Pending || x.Status == JobStatus.Running)).ToListAsync(ct);
        foreach (var job in jobs)
        {
            job.CancelRequested = true;
            job.Status = job.Status == JobStatus.Pending ? JobStatus.Cancelled : JobStatus.TimedOut;
            job.FinishedAt = Now;
            job.Summary = "Host credential revoked. Verify local workload state before retrying.";
        }
        Audit(actor, "host.revoked", host.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ExpireAsync(CancellationToken ct)
    {
        var now = Now;
        var cutoff = now - Settings.JobTimeoutSeconds * 1000L;
        await db.Jobs.Where(x => x.Status == JobStatus.Running && (x.LeaseExpiresAt <= now || x.StartedAt <= cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, JobStatus.TimedOut).SetProperty(x => x.FinishedAt, now)
                .SetProperty(x => x.Summary, "Execution lease expired. Verify local workload state before retrying."), ct);
    }

    public async Task DeregisterAsync(Guid hostId, string actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct) ?? throw new DomainException(404, "Host not found.");
        if (await db.Jobs.AnyAsync(x => x.HostId == hostId && x.Status == JobStatus.Running, ct))
            throw new DomainException(409, "Stop or cancel running work before deregistering this host.");
        if (host.DeregisteredAt.HasValue) return;
        foreach (var job in await db.Jobs.Where(x => x.HostId == hostId && x.Status == JobStatus.Pending).ToListAsync(ct))
        {
            job.Status = JobStatus.Cancelled;
            job.CancelRequested = true;
            job.FinishedAt = Now;
            job.Summary = "Host deregistered before execution.";
        }
        host.Revoked = true;
        host.DeregisteredAt = Now;
        host.MetricsJson = null;
        Audit(actor, "host.deregistered", host.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<DeploymentJob> OwnedJobAsync(Guid hostId, Guid id, Guid attemptId, CancellationToken ct)
    {
        var job = await db.Jobs.SingleOrDefaultAsync(x => x.Id == id && x.HostId == hostId, ct) ?? throw new DomainException(404, "Job not found.");
        if (job.AttemptId != attemptId) throw new DomainException(409, "Stale execution attempt.");
        return job;
    }
    private void RequireRunning(DeploymentJob job)
    {
        if (job.Status != JobStatus.Running || job.LeaseExpiresAt <= Now || job.StartedAt + Settings.JobTimeoutSeconds * 1000L <= Now)
            throw new DomainException(409, "Job is no longer running or its lease has expired.");
    }
    private void Audit(string actor, string action, Guid resource) => db.Audit.Add(new() { Actor = actor, Action = action, Resource = resource.ToString(), CreatedAt = Now });
}
