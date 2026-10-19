using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DepPilot.Agent;
using DepPilot.Master.Data;
using DepPilot.Master.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DepPilot.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void RetiredAgentCannotSilentlyCreateANewIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dep-pilot-retired-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "deregistered.json"), "{}");
            Assert.Throws<InvalidOperationException>(() => new AgentState(new AgentOptions { StateDirectory = directory }));
            Assert.False(File.Exists(Path.Combine(directory, "identity.json")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ResourceSamplerReportsBoundedValues()
    {
        var sampler = new HostResources(new AgentOptions { StateDirectory = Path.GetTempPath() });
        sampler.Sample();
        await Task.Delay(100);
        var m = sampler.Sample();
        Assert.InRange(m.CpuPercent!.Value, 0, 100);
        Assert.InRange(m.MemoryUsedBytes, 0, m.MemoryTotalBytes);
        Assert.InRange(m.DiskFreeBytes, 0, m.DiskTotalBytes);
        Assert.True(m.SampledAt > 0);
        Assert.NotEmpty(HostResources.Address());
    }

    [Fact]
    public async Task OriginalDatabaseUpgradePreservesIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dep-pilot-upgrade-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            await using var db = new RegistryDb(new DbContextOptionsBuilder<RegistryDb>().UseSqlite($"Data Source={directory}/test.db;Pooling=False").Options);
            await db.Database.EnsureCreatedAsync();
            var host = new ManagedHost { InstallationId = Guid.NewGuid(), TokenHash = "existing" };
            db.Hosts.Add(host); await db.SaveChangesAsync();
            foreach (var column in new[] { "IpAddress", "CapabilitiesJson", "MetricsJson", "DeregisteredAt" })
            {
                using var command = db.Database.GetDbConnection().CreateCommand();
                await db.Database.OpenConnectionAsync();
                command.CommandText = $"ALTER TABLE Hosts DROP COLUMN {column}";
                await command.ExecuteNonQueryAsync();
            }
            await DatabaseSetup.InitializeAsync(db);
            await DatabaseSetup.InitializeAsync(db);
            db.ChangeTracker.Clear();
            Assert.Equal(host.InstallationId, (await db.Hosts.SingleAsync()).InstallationId);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CustomCertificateTrustRejectsMismatchAndUnknownIssuer()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, certificate.ExportCertificatePem());
            using var handler = CertificateTrust.Handler(path);
            using var message = new HttpRequestMessage(HttpMethod.Get, "https://localhost");
            var validate = handler.ServerCertificateCustomValidationCallback!;
            Assert.True(validate(message, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(validate(message, certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch));
            using var another = new CertificateRequest("CN=other", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            Assert.False(validate(message, another, null, SslPolicyErrors.RemoteCertificateChainErrors));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CatalogRejectsMutatedVersionAndEscapingPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dep-pilot-catalog-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "packages"));
        Directory.CreateDirectory(Path.Combine(directory, "playbooks"));
        try
        {
            await using var db = new RegistryDb(new DbContextOptionsBuilder<RegistryDb>().UseSqlite($"Data Source={directory}/test.db;Pooling=False").Options);
            await db.Database.EnsureCreatedAsync();
            var manifest = Path.Combine(directory, "packages", "app.json");
            await File.WriteAllTextAsync(manifest, """{"name":"test","version":"1.0.0","playbook":"test.yml","prerequisites":[]}""");
            var yaml = Path.Combine(directory, "playbooks", "test.yml");
            await File.WriteAllTextAsync(yaml, "original");
            await DeploymentCatalog.SeedAsync(db, directory);
            await File.WriteAllTextAsync(yaml, "modified");
            await Assert.ThrowsAsync<InvalidOperationException>(() => DeploymentCatalog.SeedAsync(db, directory));
            await File.WriteAllTextAsync(manifest, """{"name":"test","version":"1.0.0","playbook":"../test.yml","prerequisites":[]}""");
            await Assert.ThrowsAsync<InvalidOperationException>(() => DeploymentCatalog.SeedAsync(db, directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
