namespace DepPilot.Master.Services;

public sealed class LeaseReaper(IServiceScopeFactory scopes, ILogger<LeaseReaper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DeploymentService>().ExpireAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Lease expiration sweep failed"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
