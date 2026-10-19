using DepPilot.Master.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DepPilot.Tests;

public sealed class MasterFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dep-pilot-tests-" + Guid.NewGuid().ToString("N"));
    public const string Password = "test-only-admin-password-8927";
    public const string Bootstrap = "test-only-bootstrap-token-8927-abcdefghijklmnopqrstuvwxyz";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Master:AdminPassword", Password);
        builder.UseSetting("Master:BootstrapToken", Bootstrap);
        builder.UseSetting("Master:AllowInsecureHttp", "true");
        builder.UseSetting("Master:DatabasePath", Path.Combine(directory, "test.db"));
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
