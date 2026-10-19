using System.Net;
using System.Text.Json;
using DepPilot.Contracts;
using DepPilot.Master.Data;
using DepPilot.Master.Security;
using Microsoft.EntityFrameworkCore;

namespace DepPilot.Master.Services;

public static class HostRegistry
{
    public static async Task<IResult> RegisterAsync(RegistrationRequest request, HttpContext context, RegistryDb db, MasterOptions settings, CancellationToken ct)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !TokenSecurity.EqualsSecret(auth[7..], settings.BootstrapToken)) return Results.Unauthorized();
        if (request.InstallationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Hostname) || request.Hostname.Length > 120
            || request.OperatingSystem is null || request.OperatingSystem.Length > 256 || request.AgentVersion is null || request.AgentVersion.Length > 40
            || request.Token is null || request.Token.Length != 64 || !request.Token.All(Uri.IsHexDigit)
            || request.IpAddress is null || (request.IpAddress != "" && !IPAddress.TryParse(request.IpAddress, out _))
            || request.Capabilities is { Length: > 20 } || request.Capabilities?.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 40 || !x.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) == true)
            return Results.BadRequest(new { detail = "Invalid registration fields." });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hash = TokenSecurity.Hash(request.Token);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.InstallationId == request.InstallationId, ct);
        if (host is not null)
            return host.Revoked || !TokenSecurity.EqualsSecret(host.TokenHash, hash) ? Results.Conflict() : Results.Ok(new RegistrationResponse(host.Id));
        if (await db.Hosts.AnyAsync(x => x.TokenHash == hash, ct)) return Results.Conflict();
        host = new() { InstallationId = request.InstallationId, Hostname = request.Hostname, OperatingSystem = request.OperatingSystem,
            AgentVersion = request.AgentVersion, TokenHash = hash, LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IpAddress = request.IpAddress, CapabilitiesJson = JsonSerializer.Serialize(request.Capabilities ?? []) };
        db.Hosts.Add(host);
        db.Audit.Add(new() { Actor = $"agent:{host.Id}", Action = "host.registered", Resource = host.Id.ToString(), CreatedAt = host.LastSeenAt });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new RegistrationResponse(host.Id));
    }

    public static async Task<IResult> HeartbeatAsync(Guid id, HeartbeatRequest request, RegistryDb db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var m = request.Metrics;
        if (request.AgentVersion is null || request.AgentVersion.Length > 40
            || request.Capabilities is { Length: > 20 } || request.Capabilities?.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 40 || !x.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) == true
            || (request.IpAddress is not null && !IPAddress.TryParse(request.IpAddress, out _))
            || (m is not null && (m.CpuPercent is < 0 or > 100 || m.MemoryTotalBytes <= 0 || m.MemoryUsedBytes < 0 || m.MemoryUsedBytes > m.MemoryTotalBytes
                || m.DiskTotalBytes <= 0 || m.DiskFreeBytes < 0 || m.DiskFreeBytes > m.DiskTotalBytes || m.SampledAt <= 0 || m.SampledAt > now + 300000)))
            return Results.BadRequest(new { detail = "Invalid heartbeat metrics." });
        var host = await db.Hosts.SingleAsync(x => x.Id == id && !x.Revoked, ct);
        host.LastSeenAt = now;
        host.AgentVersion = request.AgentVersion;
        if (request.IpAddress is not null) host.IpAddress = request.IpAddress;
        if (request.Capabilities is not null) host.CapabilitiesJson = JsonSerializer.Serialize(request.Capabilities);
        if (m is not null) host.MetricsJson = JsonSerializer.Serialize(m);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static HostView View(ManagedHost h, MasterOptions settings, long now) => new(h.Id, h.Hostname, h.OperatingSystem,
        h.AgentVersion, h.LastSeenAt, !h.Revoked && h.LastSeenAt > now - settings.OfflineAfterSeconds * 1000L, h.Revoked,
        h.IpAddress, JsonSerializer.Deserialize<string[]>(h.CapabilitiesJson), h.MetricsJson is null ? null : JsonSerializer.Deserialize<ResourceMetrics>(h.MetricsJson));
}
