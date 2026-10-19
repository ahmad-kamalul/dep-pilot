using DepPilot.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "DepPilotAgent");
builder.Services.AddSystemd();
var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new();
options.BootstrapToken = ReadSecret("DEP_PILOT_BOOTSTRAP_TOKEN_FILE", options.BootstrapTokenFile, options.BootstrapToken);
options.MinioRootPassword = ReadSecret("DEP_PILOT_MINIO_PASSWORD_FILE", options.MinioPasswordFile, options.MinioRootPassword);
if (!Uri.TryCreate(options.MasterUrl.TrimEnd('/') + "/", UriKind.Absolute, out var masterUri)
    || !string.IsNullOrEmpty(masterUri.UserInfo) || masterUri.AbsolutePath != "/" || !string.IsNullOrEmpty(masterUri.Query)
    || (masterUri.Scheme != "https" && !(options.AllowInsecureHttp && masterUri.Scheme == "http")))
    throw new InvalidOperationException("Agent:MasterUrl must be an HTTPS origin; HTTP requires explicit local-demo opt-in.");
if (options.BootstrapToken.Length < 32 || options.MinioRootPassword.Length < 16 || options.MinioRootUser.Length < 3
    || options.MinioApiPort is < 1024 or > 65535 || options.MinioConsolePort is < 1024 or > 65535 || options.MinioApiPort == options.MinioConsolePort
    || options.PollSeconds is < 1 or > 60 || options.HeartbeatSeconds is < 1 or > 30)
    throw new InvalidOperationException("Invalid Agent credentials, ports, or polling intervals.");
if (options.MinioStoragePath != "" && (!options.MinioStoragePath.StartsWith('/') || options.MinioStoragePath.Contains(':') || options.MinioStoragePath.Contains("..") || options.MinioStoragePath.Contains('\n')))
    throw new InvalidOperationException("MinioStoragePath must be an absolute Linux path without traversal or volume separators.");
Directory.CreateDirectory(options.StateDirectory);
builder.Logging.AddProvider(new LocalFileLogger(options.StateDirectory));
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<HostResources>();
builder.Services.AddSingleton<AgentState>();
builder.Services.AddSingleton<PlaybookRunner>();
builder.Services.AddHttpClient<MasterClient>(http => { http.BaseAddress = masterUri; http.Timeout = TimeSpan.FromSeconds(10); http.MaxResponseContentBufferSize = 1024 * 1024; })
    .ConfigurePrimaryHttpMessageHandler(() => CertificateTrust.Handler(options.ServerCertificateFile));
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();
await builder.Build().RunAsync();

static string ReadSecret(string variable, string configuredPath, string fallback)
{
    var path = Environment.GetEnvironmentVariable(variable) ?? configuredPath;
    return path.Length > 0 ? File.ReadAllText(path).Trim() : fallback;
}
