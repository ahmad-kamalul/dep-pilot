using DepPilot.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new();
options.BootstrapToken = ReadSecret("DEP_PILOT_BOOTSTRAP_TOKEN_FILE", options.BootstrapToken);
options.MinioRootPassword = ReadSecret("DEP_PILOT_MINIO_PASSWORD_FILE", options.MinioRootPassword);
if (!Uri.TryCreate(options.MasterUrl.TrimEnd('/') + "/", UriKind.Absolute, out var masterUri)
    || !string.IsNullOrEmpty(masterUri.UserInfo) || masterUri.AbsolutePath != "/" || !string.IsNullOrEmpty(masterUri.Query)
    || (masterUri.Scheme != "https" && !(options.AllowInsecureHttp && masterUri.Scheme == "http")))
    throw new InvalidOperationException("Agent:MasterUrl must be an HTTPS origin; HTTP requires explicit local-demo opt-in.");
if (options.BootstrapToken.Length < 32 || options.MinioRootPassword.Length < 16 || options.MinioRootUser.Length < 3
    || options.MinioApiPort is < 1024 or > 65535 || options.MinioConsolePort is < 1024 or > 65535 || options.MinioApiPort == options.MinioConsolePort
    || options.PollSeconds is < 1 or > 60 || options.HeartbeatSeconds is < 1 or > 30)
    throw new InvalidOperationException("Invalid Agent credentials, ports, or polling intervals.");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<AgentState>();
builder.Services.AddSingleton<PlaybookRunner>();
builder.Services.AddHttpClient<MasterClient>(http => { http.BaseAddress = masterUri; http.Timeout = TimeSpan.FromSeconds(10); http.MaxResponseContentBufferSize = 1024 * 1024; })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();
await builder.Build().RunAsync();

static string ReadSecret(string variable, string fallback) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } path ? File.ReadAllText(path).Trim() : fallback;
