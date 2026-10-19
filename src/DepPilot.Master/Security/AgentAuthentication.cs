using System.Security.Claims;
using System.Text.Encodings.Web;
using DepPilot.Master.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DepPilot.Master.Security;

public sealed class AgentAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, RegistryDb db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header[7..];
        if (token.Length != 64) return AuthenticateResult.Fail("Invalid credential.");
        var hash = TokenSecurity.Hash(token);
        var retirementRetry = HttpMethods.IsPost(Request.Method) && Request.Path.Equals("/api/agent/deregister", StringComparison.OrdinalIgnoreCase);
        var host = await db.Hosts.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && (!x.Revoked || (retirementRetry && x.DeregisteredAt != null)), Context.RequestAborted);
        if (host is null) return AuthenticateResult.Fail("Invalid credential.");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, host.Id.ToString())], Scheme.Name);
        return AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
