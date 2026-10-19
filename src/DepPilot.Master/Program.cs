using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DepPilot.Contracts;
using DepPilot.Master.Data;
using DepPilot.Master.Security;
using DepPilot.Master.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Master").Get<MasterOptions>() ?? new();
// Secret files take precedence; Compose mounts them without baking values into images.
settings.AdminPassword = Secret("DEP_PILOT_ADMIN_PASSWORD_FILE", settings.AdminPassword);
settings.BootstrapToken = Secret("DEP_PILOT_BOOTSTRAP_TOKEN_FILE", settings.BootstrapToken);
if (settings.AdminPassword.Length < 16 || settings.BootstrapToken.Length < 32 || string.IsNullOrWhiteSpace(settings.AdminUser)
    || settings.LeaseSeconds < 20 || settings.JobTimeoutSeconds < settings.LeaseSeconds || settings.JobTimeoutSeconds > 3600 || settings.OfflineAfterSeconds < 30)
    throw new InvalidOperationException("Configure a 16+ character admin password, 32+ character bootstrap token, and valid timing options.");
builder.Services.AddSingleton<IOptions<MasterOptions>>(Options.Create(settings));
var databasePath = Path.GetFullPath(settings.DatabasePath);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
builder.Services.AddDbContext<RegistryDb>(o => o.UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 15 }.ToString()));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetDirectoryName(databasePath)!, "keys")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<DeploymentService>();
builder.Services.AddHostedService<LeaseReaper>();
builder.Services.AddRazorPages(o => o.Conventions.AuthorizeFolder("/").AllowAnonymousToPage("/Login"));
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SecurePolicy = settings.AllowInsecureHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Login";
        o.Cookie.Name = "DepPilot.Admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = settings.AllowInsecureHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromHours(4);
        o.Events.OnRedirectToLogin = c => { if (c.Request.Path.StartsWithSegments("/api")) c.Response.StatusCode = 401; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
    })
    .AddScheme<AuthenticationSchemeOptions, AgentAuthentication>("Agent", _ => { });
builder.Services.AddAuthorization(o => o.AddPolicy("Agent", p => p.AddAuthenticationSchemes("Agent").RequireAuthenticatedUser()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("enrollment", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 32768);
builder.Services.AddSingleton(new AdminCredential(settings.AdminUser, new PasswordHasher<string>().HashPassword(settings.AdminUser, settings.AdminPassword)));
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RegistryDb>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    var content = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "playbooks", "minio.yml"));
    var hash = TokenSecurity.Hash(content);
    var existing = await db.Playbooks.SingleOrDefaultAsync(x => x.Name == "Deploy standalone MinIO" && x.Version == "1.0.0");
    if (existing is null) { db.Playbooks.Add(new() { Name = "Deploy standalone MinIO", Version = "1.0.0", Content = content, Sha256 = hash }); await db.SaveChangesAsync(); }
    else if (existing.Sha256 != hash) throw new InvalidOperationException("Playbook content changed without a version increment.");
}
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (!settings.AllowInsecureHttp && !context.Request.IsHttps) { context.Response.StatusCode = 400; await context.Response.WriteAsync("HTTPS is required."); return; }
    try { await next(context); }
    catch (DomainException ex) { await Results.Problem(statusCode: ex.Status, detail: ex.Message).ExecuteAsync(context); }
    catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, detail: "Invalid CSRF token.").ExecuteAsync(context); }
});
if (!settings.AllowInsecureHttp) app.UseHsts();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/admin") && !HttpMethods.IsGet(context.Request.Method) && context.User.Identity?.IsAuthenticated == true)
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next(context);
});
app.MapRazorPages();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.MapPost("/api/agents/register", async (RegistrationRequest request, HttpContext context, RegistryDb db, CancellationToken ct) =>
{
    var auth = context.Request.Headers.Authorization.ToString();
    if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !TokenSecurity.EqualsSecret(auth[7..], settings.BootstrapToken)) return Results.Unauthorized();
    if (request.InstallationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Hostname) || request.Hostname.Length > 120 || request.OperatingSystem is null || request.OperatingSystem.Length > 256 || request.AgentVersion is null || request.AgentVersion.Length > 40 || request.Token is null || request.Token.Length != 64 || !request.Token.All(Uri.IsHexDigit))
        return Results.BadRequest(new { detail = "Invalid registration fields." });
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var hash = TokenSecurity.Hash(request.Token);
    var host = await db.Hosts.SingleOrDefaultAsync(x => x.InstallationId == request.InstallationId, ct);
    if (host is not null)
    {
        if (host.Revoked || !TokenSecurity.EqualsSecret(host.TokenHash, hash)) return Results.Conflict(new { detail = "Installation is revoked or belongs to a different credential." });
        return Results.Ok(new RegistrationResponse(host.Id));
    }
    if (await db.Hosts.AnyAsync(x => x.TokenHash == hash, ct)) return Results.Conflict();
    host = new() { InstallationId = request.InstallationId, Hostname = request.Hostname, OperatingSystem = request.OperatingSystem, AgentVersion = request.AgentVersion, TokenHash = hash, LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
    db.Hosts.Add(host);
    db.Audit.Add(new() { Actor = $"agent:{host.Id}", Action = "host.registered", Resource = host.Id.ToString(), CreatedAt = host.LastSeenAt });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Ok(new RegistrationResponse(host.Id));
}).RequireRateLimiting("enrollment");

var agent = app.MapGroup("/api/agent").RequireAuthorization("Agent");
agent.MapPost("/heartbeat", async (HeartbeatRequest request, HttpContext context, RegistryDb db, CancellationToken ct) =>
{
    if (request.AgentVersion is null || request.AgentVersion.Length > 40) return Results.BadRequest();
    var id = HostId(context);
    await db.Hosts.Where(x => x.Id == id && !x.Revoked).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).SetProperty(x => x.AgentVersion, request.AgentVersion), ct);
    return Results.NoContent();
});
agent.MapPost("/jobs/claim", async (HttpContext c, DeploymentService service, CancellationToken ct) =>
{
    var assignment = await service.ClaimAsync(HostId(c), ct);
    return assignment is null ? Results.NoContent() : Results.Ok(assignment);
});
agent.MapPost("/jobs/{id:guid}/lease", async (Guid id, LeaseRequest r, HttpContext c, DeploymentService s, CancellationToken ct) => Results.Ok(await s.RenewAsync(HostId(c), id, r.AttemptId, ct)));
agent.MapPost("/jobs/{id:guid}/complete", async (Guid id, CompletionRequest r, HttpContext c, DeploymentService s, CancellationToken ct) => { await s.CompleteAsync(HostId(c), id, r, ct); return Results.NoContent(); });
agent.MapPost("/jobs/{id:guid}/logs", async (Guid id, LogRequest r, HttpContext c, DeploymentService s, CancellationToken ct) => { await s.AppendLogAsync(HostId(c), id, r, ct); return Results.NoContent(); });
agent.MapGet("/jobs/{id:guid}/playbook", async (Guid id, HttpContext c, RegistryDb db, CancellationToken ct) =>
{
    var hostId = HostId(c);
    var content = await (from job in db.Jobs join d in db.Deployments on job.DeploymentId equals d.Id join p in db.Playbooks on d.PlaybookId equals p.Id where job.Id == id && job.HostId == hostId && job.Status == JobStatus.Running select p.Content).SingleOrDefaultAsync(ct);
    return content is null ? Results.NotFound() : Results.Text(content, "text/yaml", Encoding.UTF8);
});

var admin = app.MapGroup("/api/admin").RequireAuthorization();
admin.MapGet("/csrf", (HttpContext c, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(c).RequestToken }));
admin.MapGet("/dashboard", async (RegistryDb db, CancellationToken ct) =>
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var hosts = await db.Hosts.OrderBy(x => x.Hostname).Select(x => new HostView(x.Id, x.Hostname, x.OperatingSystem, x.AgentVersion, x.LastSeenAt, !x.Revoked && x.LastSeenAt > now - settings.OfflineAfterSeconds * 1000L, x.Revoked)).ToArrayAsync(ct);
    var playbooks = await db.Playbooks.Select(x => new PlaybookView(x.Id, x.Name, x.Version, x.Sha256)).ToArrayAsync(ct);
    var jobs = await (from j in db.Jobs join h in db.Hosts on j.HostId equals h.Id join d in db.Deployments on j.DeploymentId equals d.Id orderby j.CreatedAt descending select new JobView(j.Id, d.Id, d.Name, h.Id, h.Hostname, j.Status, j.CancelRequested, j.CreatedAt, j.StartedAt, j.FinishedAt, j.ExitCode, j.Summary)).Take(200).ToArrayAsync(ct);
    return new DashboardView(hosts, playbooks, jobs);
});
admin.MapPost("/deployments", async (DeploymentRequest r, HttpContext c, DeploymentService s, CancellationToken ct) => Results.Ok(new { id = await s.CreateAsync(r, c.User.Identity!.Name!, ct) }));
admin.MapPost("/jobs/{id:guid}/cancel", async (Guid id, HttpContext c, DeploymentService s, CancellationToken ct) => { await s.CancelAsync(id, c.User.Identity!.Name!, ct); return Results.NoContent(); });
admin.MapPost("/hosts/{id:guid}/revoke", async (Guid id, HttpContext c, DeploymentService s, CancellationToken ct) => { await s.RevokeAsync(id, c.User.Identity!.Name!, ct); return Results.NoContent(); });
admin.MapGet("/jobs/{id:guid}/logs", async (Guid id, RegistryDb db, CancellationToken ct) => Results.Ok(await db.Logs.Where(x => x.JobId == id).OrderBy(x => x.Sequence).Select(x => new { x.Sequence, x.CreatedAt, x.Text }).ToArrayAsync(ct)));
admin.MapGet("/audit", async (RegistryDb db, CancellationToken ct) => Results.Ok(await db.Audit.OrderByDescending(x => x.Id).Take(200).ToArrayAsync(ct)));
app.Run();

static Guid HostId(HttpContext c) => Guid.Parse(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
static string Secret(string variable, string fallback) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } path ? File.ReadAllText(path).Trim() : fallback;
public sealed record AdminCredential(string Username, string PasswordHash);
public partial class Program;
