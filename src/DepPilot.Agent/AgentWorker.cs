using System.Net;
using DepPilot.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DepPilot.Agent;

public sealed class AgentWorker(MasterClient master, AgentState state, AgentOptions options, PlaybookRunner runner, ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (state.Identity.HostId is null) { await master.RegisterAsync(stoppingToken); logger.LogInformation("Agent registered as {HostId}", state.Identity.HostId); }
                    if (state.LoadResult() is { } pending) await DeliverAsync(pending, stoppingToken);
                    else if (await master.ClaimAsync(stoppingToken) is { } job) await ExecuteJobAsync(job, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Agent cycle failed: {Type}. Retrying with jitter.", ex.GetType().Name);
                }
                await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task ExecuteJobAsync(JobAssignment job, CancellationToken stoppingToken)
    {
        logger.LogInformation("Executing job {JobId}, attempt {AttemptId}", job.JobId, job.AttemptId);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var renewal = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var cancelRequested = false;
        var leaseLost = false;
        async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), renewal.Token);
                    var response = await master.RenewAsync(job, renewal.Token);
                    if (response.CancelRequested) { cancelRequested = true; execution.Cancel(); return; }
                }
            }
            catch (OperationCanceledException) when (renewal.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogWarning("Lease renewal failed ({Type}); stopping local execution", ex.GetType().Name); leaseLost = true; execution.Cancel(); }
        }
        var renewTask = RenewAsync();
        PendingResult result;
        try
        {
            var playbook = await master.PlaybookAsync(job.JobId, execution.Token);
            var outcome = await runner.RunAsync(job, playbook, execution.Token);
            var status = leaseLost || outcome.TimedOut ? JobStatus.TimedOut : cancelRequested || stoppingToken.IsCancellationRequested ? JobStatus.Cancelled : outcome.ExitCode == 0 ? JobStatus.Succeeded : JobStatus.Failed;
            var summary = status switch
            {
                JobStatus.Succeeded => "Playbook completed and MinIO readiness check passed.",
                JobStatus.Cancelled => "Execution cancelled. Previously applied changes remain; inspect the local workload.",
                JobStatus.TimedOut => "Execution deadline or lease lost. Inspect the local workload before retrying.",
                _ => "Playbook failed. Review job output for details."
            };
            result = new(job.JobId, new(job.AttemptId, status, outcome.ExitCode, summary), outcome.Output);
        }
        catch (Exception ex)
        {
            var status = leaseLost ? JobStatus.TimedOut : execution.IsCancellationRequested ? JobStatus.Cancelled : JobStatus.Failed;
            result = new(job.JobId, new(job.AttemptId, status, null, "Execution could not finish. Inspect Agent configuration and local workload."), "Execution error: " + ex.GetType().Name);
        }
        // Persist before network delivery. Keep renewing until output and result are acknowledged.
        state.SaveResult(result);
        try { await DeliverAsync(result, stoppingToken); }
        finally { renewal.Cancel(); await renewTask; }
    }

    private async Task DeliverAsync(PendingResult result, CancellationToken ct)
    {
        try
        {
            // Log ingestion is best-effort after a restart; terminal completion remains retryable.
            try { await master.LogsAsync(result, ct); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { }
            await master.CompleteAsync(result, ct);
            state.ClearResult();
            logger.LogInformation("Result acknowledged for job {JobId}: {Status}", result.JobId, result.Completion.Status);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            state.QuarantineResult();
            logger.LogWarning("Master rejected stale result for {JobId}; saved locally for inspection", result.JobId);
        }
    }
}

public sealed class HeartbeatWorker(MasterClient master, AgentState state, AgentOptions options, ILogger<HeartbeatWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (state.Identity.HostId.HasValue)
                {
                    try { await master.HeartbeatAsync(stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning("Heartbeat failed ({Type})", ex.GetType().Name); }
                }
                await Task.Delay(TimeSpan.FromSeconds(options.HeartbeatSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
