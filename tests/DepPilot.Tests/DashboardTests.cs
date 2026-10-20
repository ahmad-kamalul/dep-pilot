using System.Net;
using System.Net.Http.Json;
using DepPilot.Contracts;
using DepPilot.Master.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepPilot.Tests;

public sealed partial class ApiTests
{
    [Fact]
    public async Task BusyHostTracksPendingRunningAndTerminalJobs()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        async Task<DashboardView> Dashboard() => (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        var dashboard = await Dashboard();
        Assert.False(Assert.Single(dashboard.Hosts).Busy);
        var request = new DeploymentRequest("Busy host", dashboard.Playbooks[0].Id, [id]);
        (await admin.PostAsJsonAsync("/api/admin/deployments", request, Json)).EnsureSuccessStatusCode();
        dashboard = await Dashboard();
        Assert.True(Assert.Single(dashboard.Hosts).Busy);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/deployments", request, Json)).StatusCode);
        (await admin.PostAsJsonAsync($"/api/admin/jobs/{dashboard.Jobs[0].Id}/cancel", new { })).EnsureSuccessStatusCode();
        Assert.False(Assert.Single((await Dashboard()).Hosts).Busy);
        var job = await CreateAndClaim(admin, agent, id);
        Assert.True(Assert.Single((await Dashboard()).Hosts).Busy);
        (await admin.PostAsJsonAsync($"/api/admin/jobs/{job.JobId}/cancel", new { })).EnsureSuccessStatusCode();
        Assert.True(Assert.Single((await Dashboard()).Hosts).Busy);
        (await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/complete", new CompletionRequest(job.AttemptId, JobStatus.Cancelled, null, "Cancelled"), Json)).EnsureSuccessStatusCode();
        Assert.False(Assert.Single((await Dashboard()).Hosts).Busy);
    }

    [Fact]
    public async Task BusyHostIncludesActiveWorkOutsideLatestHistoryPage()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        (await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("Older pending job", dashboard.Playbooks[0].Id, [id]), Json)).EnsureSuccessStatusCode();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDb>();
            var pending = await db.Jobs.SingleAsync();
            db.Jobs.AddRange(Enumerable.Range(1, 200).Select(i => new DeploymentJob
            {
                HostId = id, DeploymentId = pending.DeploymentId, Status = JobStatus.Succeeded, CreatedAt = pending.CreatedAt + i
            }));
            await db.SaveChangesAsync();
        }
        dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        Assert.Equal(200, dashboard.Jobs.Length);
        Assert.All(dashboard.Jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        Assert.True(Assert.Single(dashboard.Hosts).Busy);
    }
}
