using DepPilot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DepPilot.Master.Data;

public sealed class RegistryDb(DbContextOptions<RegistryDb> options) : DbContext(options)
{
    public DbSet<ManagedHost> Hosts => Set<ManagedHost>();
    public DbSet<Playbook> Playbooks => Set<Playbook>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<DeploymentJob> Jobs => Set<DeploymentJob>();
    public DbSet<JobLog> Logs => Set<JobLog>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ManagedHost>().HasIndex(x => x.InstallationId).IsUnique();
        b.Entity<ManagedHost>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<Playbook>().HasIndex(x => new { x.Name, x.Version }).IsUnique();
        b.Entity<DeploymentJob>().HasIndex(x => new { x.HostId, x.Status });
        b.Entity<DeploymentJob>().HasOne<ManagedHost>().WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DeploymentJob>().HasOne<Deployment>().WithMany().HasForeignKey(x => x.DeploymentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Deployment>().HasOne<Playbook>().WithMany().HasForeignKey(x => x.PlaybookId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JobLog>().HasIndex(x => new { x.JobId, x.Sequence }).IsUnique();
        b.Entity<JobLog>().HasOne<DeploymentJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ManagedHost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstallationId { get; set; }
    public string Hostname { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public long LastSeenAt { get; set; }
    public bool Revoked { get; set; }
    public string IpAddress { get; set; } = "";
    public string CapabilitiesJson { get; set; } = "[]";
    public string? MetricsJson { get; set; }
    public long? DeregisteredAt { get; set; }
}
public sealed class Playbook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Content { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
public sealed class Deployment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid PlaybookId { get; set; }
    public long CreatedAt { get; set; }
    public string RequestedBy { get; set; } = "";
}
public sealed class DeploymentJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeploymentId { get; set; }
    public Guid HostId { get; set; }
    public JobStatus Status { get; set; }
    public Guid? AttemptId { get; set; }
    public long CreatedAt { get; set; }
    public long? StartedAt { get; set; }
    public long? FinishedAt { get; set; }
    public long? LeaseExpiresAt { get; set; }
    public bool CancelRequested { get; set; }
    public int? ExitCode { get; set; }
    public string? Summary { get; set; }
}
public sealed class JobLog
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public long Sequence { get; set; }
    public long CreatedAt { get; set; }
    public string Text { get; set; } = "";
}
public sealed class AuditEntry
{
    public long Id { get; set; }
    public long CreatedAt { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Resource { get; set; } = "";
}
