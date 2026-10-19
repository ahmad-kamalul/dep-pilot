using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DepPilot.Contracts;
using Xunit;

namespace DepPilot.Tests;

public sealed class ApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static HttpClient Client(MasterFactory factory) => factory.CreateClient(new() { AllowAutoRedirect = false });

    private static async Task<(HttpClient Client, Guid Id, RegistrationRequest Request)> Enroll(MasterFactory factory)
    {
        var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", MasterFactory.Bootstrap);
        var request = new RegistrationRequest(Guid.NewGuid(), "test-host", "Linux", "1.0.0", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var response = await client.PostAsJsonAsync("/api/agents/register", request, Json);
        response.EnsureSuccessStatusCode();
        var registered = (await response.Content.ReadFromJsonAsync<RegistrationResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", request.Token);
        return (client, registered.HostId, request);
    }

    private static async Task<HttpClient> Admin(MasterFactory factory)
    {
        var client = Client(factory);
        var page = await client.GetStringAsync("/Login");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = "admin", ["Password"] = MasterFactory.Password, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/admin/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return client;
    }

    private static async Task<JobAssignment> CreateAndClaim(HttpClient admin, HttpClient agent, Guid host)
    {
        var dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        var response = await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("Integration rollout", dashboard.Playbooks[0].Id, [host]), Json);
        response.EnsureSuccessStatusCode();
        var claimed = await agent.PostAsJsonAsync("/api/agent/jobs/claim", new { });
        claimed.EnsureSuccessStatusCode();
        return (await claimed.Content.ReadFromJsonAsync<JobAssignment>(Json))!;
    }

    [Fact]
    public async Task AnonymousCannotReadAdminOrClaimWork()
    {
        using var f = new MasterFactory(); using var c = Client(f);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/agent/jobs/claim", new { })).StatusCode);
    }

    [Fact]
    public async Task EnrollmentRequiresBootstrapAndDoesNotPermitIdentityTakeover()
    {
        using var f = new MasterFactory(); var (agent, id, request) = await Enroll(f); using var a = agent;
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/agents/register", request, Json)).StatusCode);
        a.DefaultRequestHeaders.Authorization = new("Bearer", MasterFactory.Bootstrap);
        var replay = await a.PostAsJsonAsync("/api/agents/register", request, Json);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<RegistrationResponse>(Json))!.HostId);
        var takeover = request with { Token = new string('B', 64) };
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/agents/register", takeover, Json)).StatusCode);
    }

    [Fact]
    public async Task AgentCannotUseAdminEndpointsAndAdminCannotClaimAgentJobs()
    {
        using var f = new MasterFactory(); var (a, _, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync("/api/agent/jobs/claim", new { })).StatusCode);
    }

    [Fact]
    public async Task AdminMutationRequiresCsrfToken()
    {
        using var f = new MasterFactory(); using var admin = await Admin(f);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var response = await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("test", Guid.NewGuid(), [Guid.NewGuid()]), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CSRF", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FullLifecyclePersistsLogsAndAcceptsDuplicateResult()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var job = await CreateAndClaim(admin, agent, id);
        var content = await agent.GetStringAsync($"/api/agent/jobs/{job.JobId}/playbook");
        Assert.Equal(job.Sha256, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))));
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/logs", new LogRequest(job.AttemptId, 0, "MinIO ready"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/logs", new LogRequest(job.AttemptId, 0, "MinIO ready"), Json)).StatusCode);
        var completion = new CompletionRequest(job.AttemptId, JobStatus.Succeeded, 0, "Ready");
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/complete", completion, Json)).StatusCode);
        var dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        Assert.Equal(JobStatus.Succeeded, Assert.Single(dashboard.Jobs).Status);
        var logs = await admin.GetFromJsonAsync<JsonElement[]>($"/api/admin/jobs/{job.JobId}/logs");
        Assert.Single(logs!);
        var audit = await admin.GetStringAsync("/api/admin/audit");
        Assert.Contains("job.completed", audit);
    }

    [Fact]
    public async Task ConcurrentClaimsYieldExactlyOneAssignment()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        (await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("race", dashboard.Playbooks[0].Id, [id]), Json)).EnsureSuccessStatusCode();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => agent.PostAsJsonAsync("/api/agent/jobs/claim", new { })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task CrossHostCannotReadPlaybookOrReportResults()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); var (b, _, _) = await Enroll(f); using var agent = a; using var other = b; using var admin = await Admin(f);
        var job = await CreateAndClaim(admin, agent, id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/agent/jobs/{job.JobId}/playbook")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/complete", new CompletionRequest(job.AttemptId, JobStatus.Succeeded, 0, "forged"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/logs", new LogRequest(job.AttemptId, 0, "forged"), Json)).StatusCode);
    }

    [Fact]
    public async Task StaleAttemptsAndInvalidSuccessAreRejected()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var job = await CreateAndClaim(admin, agent, id);
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/lease", new LeaseRequest(Guid.NewGuid()), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/complete", new CompletionRequest(job.AttemptId, JobStatus.Succeeded, 1, "invalid"), Json)).StatusCode);
    }

    [Fact]
    public async Task DuplicateActiveDeploymentIsRejected()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var job = await CreateAndClaim(admin, agent, id);
        var response = await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("duplicate", job.PlaybookId, [id]), Json);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CancellationIsDeliveredThroughLeaseRenewal()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var job = await CreateAndClaim(admin, agent, id);
        (await admin.PostAsJsonAsync($"/api/admin/jobs/{job.JobId}/cancel", new { })).EnsureSuccessStatusCode();
        var response = await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/lease", new LeaseRequest(job.AttemptId), Json);
        Assert.True((await response.Content.ReadFromJsonAsync<LeaseResponse>(Json))!.CancelRequested);
        (await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/complete", new CompletionRequest(job.AttemptId, JobStatus.Cancelled, null, "Stopped"), Json)).EnsureSuccessStatusCode();
        Assert.Equal(JobStatus.Cancelled, Assert.Single((await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Jobs).Status);
    }

    [Fact]
    public async Task RevokedHostCannotHeartbeatOrReenroll()
    {
        using var f = new MasterFactory(); var (a, id, request) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{id}/revoke", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/agent/heartbeat", new HeartbeatRequest("1"), Json)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MasterFactory.Bootstrap);
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync("/api/agents/register", request, Json)).StatusCode);
    }

    [Fact]
    public async Task CancelledPendingJobCannotBeClaimed()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        (await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("cancel before claim", dashboard.Playbooks[0].Id, [id]), Json)).EnsureSuccessStatusCode();
        var pending = Assert.Single((await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Jobs);
        (await admin.PostAsJsonAsync($"/api/admin/jobs/{pending.Id}/cancel", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/agent/jobs/claim", new { })).StatusCode);
        Assert.Equal(JobStatus.Cancelled, Assert.Single((await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Jobs).Status);
    }

    [Fact]
    public async Task InvalidTargetRejectsWholeDeploymentWithoutPartialJobs()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        var response = await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("atomic targets", dashboard.Playbooks[0].Id, [id, Guid.NewGuid()]), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Jobs);
    }

    [Fact]
    public async Task RepeatedHostSelectionCreatesOneJob()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var dashboard = (await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!;
        (await admin.PostAsJsonAsync("/api/admin/deployments", new DeploymentRequest("deduplicate", dashboard.Playbooks[0].Id, [id, id]), Json)).EnsureSuccessStatusCode();
        Assert.Single((await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Jobs);
    }

    [Fact]
    public async Task RevocationTerminatesRunningJobAndRejectsResult()
    {
        using var f = new MasterFactory(); var (a, id, _) = await Enroll(f); using var agent = a; using var admin = await Admin(f);
        var job = await CreateAndClaim(admin, agent, id);
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{id}/revoke", new { })).EnsureSuccessStatusCode();
        Assert.Equal(JobStatus.TimedOut, Assert.Single((await admin.GetFromJsonAsync<DashboardView>("/api/admin/dashboard", Json))!.Jobs).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync($"/api/agent/jobs/{job.JobId}/complete", new CompletionRequest(job.AttemptId, JobStatus.Succeeded, 0, "late"), Json)).StatusCode);
    }
}
