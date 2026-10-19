using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DepPilot.Contracts;

namespace DepPilot.Agent;

public sealed class MasterClient(HttpClient http, AgentOptions options, AgentState state, HostResources resources)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public const string Version = "1.1.0";
    public async Task RegisterAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/register")
        {
            Content = JsonContent.Create(new RegistrationRequest(state.Identity.InstallationId, options.Hostname, RuntimeInformation.OSDescription, Version, state.Identity.Token, HostResources.Address(), HostResources.Capabilities), options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BootstrapToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RegistrationResponse>(Json, ct) ?? throw new InvalidOperationException("Missing enrollment response.");
        state.SetHost(result.HostId);
    }
    public async Task HeartbeatAsync(CancellationToken ct) { using var response = await SendAsync("api/agent/heartbeat", new HeartbeatRequest(Version, resources.Sample(), HostResources.Address(), HostResources.Capabilities), ct); }
    public async Task<JobAssignment?> ClaimAsync(CancellationToken ct)
    {
        using var response = await SendAsync("api/agent/jobs/claim", new { }, ct);
        return response.StatusCode == HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<JobAssignment>(Json, ct);
    }
    public async Task<string> PlaybookAsync(Guid id, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, $"api/agent/jobs/{id}/playbook");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length > 1024 * 1024) throw new InvalidOperationException("Playbook exceeds size limit.");
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
    public async Task<LeaseResponse> RenewAsync(JobAssignment job, CancellationToken ct)
    {
        using var response = await SendAsync($"api/agent/jobs/{job.JobId}/lease", new LeaseRequest(job.AttemptId), ct);
        return (await response.Content.ReadFromJsonAsync<LeaseResponse>(Json, ct))!;
    }
    public async Task LogsAsync(PendingResult result, CancellationToken ct)
    {
        const int chunk = 8000;
        for (var offset = 0; offset < result.Output.Length; offset += chunk)
        {
            using var response = await SendAsync($"api/agent/jobs/{result.JobId}/logs", new LogRequest(result.Completion.AttemptId, offset / chunk, result.Output.Substring(offset, Math.Min(chunk, result.Output.Length - offset))), ct);
        }
    }
    public async Task CompleteAsync(PendingResult result, CancellationToken ct) { using var response = await SendAsync($"api/agent/jobs/{result.JobId}/complete", result.Completion, ct); }
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Identity.Token);
        return request;
    }
    private async Task<HttpResponseMessage> SendAsync<T>(string path, T body, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body, options: Json);
        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) { var status = response.StatusCode; response.Dispose(); throw new HttpRequestException($"Master request failed ({(int)status}).", null, status); }
        return response;
    }
}
