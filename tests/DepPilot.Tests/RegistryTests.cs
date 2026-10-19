using System.Net;
using System.Net.Http.Json;
using DepPilot.Contracts;
using Xunit;

namespace DepPilot.Tests;

public sealed partial class ApiTests
{
    [Fact]
    public async Task HeartbeatPersistsValidatedResourceMetrics()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var metrics = new ResourceMetrics(25.5, 1024, 4096, 2048, 8192, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        (await agent.PostAsJsonAsync("/api/agent/heartbeat", new HeartbeatRequest("1.1.0", metrics, "10.20.0.5"), Json)).EnsureSuccessStatusCode();
        var host = Assert.Single((await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Hosts);
        Assert.Equal(id, host.Id);
        Assert.Equal("10.20.0.5", host.IpAddress);
        Assert.Equal(metrics, host.Metrics);
        var invalid = metrics with { CpuPercent = 101 };
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync("/api/agent/heartbeat", new HeartbeatRequest("1", invalid), Json)).StatusCode);
        invalid = metrics with { MemoryUsedBytes = 5000 };
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync("/api/agent/heartbeat", new HeartbeatRequest("1", invalid), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync("/api/agent/heartbeat", new HeartbeatRequest("1", metrics, "bad-ip"), Json)).StatusCode);
    }

    [Fact]
    public async Task RegistrationStoresMetadataAndRejectsInvalidCapabilities()
    {
        using var f = new MasterFactory(); var (a, _, original) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        agent.DefaultRequestHeaders.Authorization = new("Bearer", MasterFactory.Bootstrap);
        var request = original with { InstallationId = Guid.NewGuid(), Token = new string('C', 64), IpAddress = "10.0.0.9", Capabilities = ["linux", "ansible", "docker"] };
        (await agent.PostAsJsonAsync("/api/agents/register", request, Json)).EnsureSuccessStatusCode();
        var hosts = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Hosts;
        Assert.Equal(request.Capabilities, hosts.Single(h => h.IpAddress == "10.0.0.9").Capabilities);
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync("/api/agents/register", request with { Capabilities = ["bad capability"] }, Json)).StatusCode);
    }

    [Fact]
    public async Task DeregistrationCancelsPendingWorkAndPreservesHistory()
    {
        using var f = new MasterFactory(); var (a, id, request) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        (await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("pending", dashboard.Playbooks[0].Id, [id]), Json)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{id}/deregister", new { })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{id}/deregister", new { })).EnsureSuccessStatusCode();
        dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        Assert.Empty(dashboard.Hosts);
        Assert.Equal(JobStatus.Cancelled, Assert.Single(dashboard.Jobs).Status);
        Assert.Contains("host.deregistered", await admin.GetStringAsync("/api/admin/audit"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/agent/jobs/claim", new { })).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new("Bearer", MasterFactory.Bootstrap);
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync("/api/agents/register", request, Json)).StatusCode);
    }

    [Fact]
    public async Task DeregistrationRejectsRunningWorkAndAgentCannotRemoveAnotherHost()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); var (b, _, _) = await Enroll(f);
        using var agent = a; using var other = b; using var admin = await Admin(f);
        await CreateAndClaim(admin, agent, id);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/hosts/{id}/deregister", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync($"/api/admin/hosts/{id}/deregister", new { })).StatusCode);
        (await other.PostAsJsonAsync("/api/agent/deregister", new { })).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync("/api/agent/deregister", new { })).EnsureSuccessStatusCode();
        Assert.Single((await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Hosts);
    }
}
