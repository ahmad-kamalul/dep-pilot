namespace DepPilot.Contracts;

public enum JobStatus { Pending, Running, Succeeded, Failed, TimedOut, Cancelled }
public sealed record RegistrationRequest(Guid InstallationId, string Hostname, string OperatingSystem, string AgentVersion, string Token, string IpAddress = "", string[]? Capabilities = null);
public sealed record RegistrationResponse(Guid HostId);
public sealed record ResourceMetrics(double? CpuPercent, long MemoryUsedBytes, long MemoryTotalBytes, long DiskFreeBytes, long DiskTotalBytes, long SampledAt);
public sealed record HeartbeatRequest(string AgentVersion, ResourceMetrics? Metrics = null, string? IpAddress = null, string[]? Capabilities = null);
public sealed record DeploymentRequest(string Name, Guid PlaybookId, Guid[] HostIds);
public sealed record JobAssignment(Guid JobId, Guid AttemptId, Guid PlaybookId, string PlaybookName, string Version, string Sha256, int TimeoutSeconds);
public sealed record LeaseRequest(Guid AttemptId);
public sealed record LeaseResponse(bool CancelRequested);
public sealed record CompletionRequest(Guid AttemptId, JobStatus Status, int? ExitCode, string Summary);
public sealed record LogRequest(Guid AttemptId, long Sequence, string Text);
public sealed record HostView(Guid Id, string Hostname, string OperatingSystem, string AgentVersion, long LastSeenAt, bool Online, bool Revoked, string IpAddress = "", string[]? Capabilities = null, ResourceMetrics? Metrics = null, bool Busy = false);
public sealed record PlaybookView(Guid Id, string Name, string Version, string Sha256);
public sealed record JobView(Guid Id, Guid DeploymentId, string DeploymentName, Guid HostId, string Hostname, JobStatus Status, bool CancelRequested, long CreatedAt, long? StartedAt, long? FinishedAt, int? ExitCode, string? Summary);
public sealed record DashboardView(HostView[] Hosts, PlaybookView[] Playbooks, JobView[] Jobs);
