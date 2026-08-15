using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Proxytrace.Application.Auth;
using Proxytrace.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Auth;

internal interface IAuthUserResolver
{
    Task<IUser?> Resolve(TokenValidatedContext context, ClaimsPrincipal principal);
}

internal class LocalUserResolver : IAuthUserResolver
{
    private readonly IRepository<IUser> users;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalUserResolver"/> class.
    /// </summary>
    public LocalUserResolver(IRepository<IUser> users)
    {
        this.users = users;
    }

    /// <summary>
    /// Resolves.
    /// </summary>
    public async Task<IUser?> Resolve(TokenValidatedContext context, ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue("sub")
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId))
        {
            context.Fail("Invalid sub.");
            return null;
        }

        var user = await users.FindAsync(userId, context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.Fail("Unknown user.");
            return null;
        }

        return user;
    }
}

internal class JitUserResolver : IAuthUserResolver
{
    private readonly IJitUserProvisioner provisioner;
    private readonly AuthOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="JitUserResolver"/> class.
    /// </summary>
    public JitUserResolver(IJitUserProvisioner provisioner, AuthOptions options)
    {
        this.provisioner = provisioner;
        this.options = options;
    }

    /// <summary>
    /// Resolves.
    /// </summary>
    public async Task<IUser?> Resolve(TokenValidatedContext context, ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirstValue("iss")
                     ?? principal.Claims.FirstOrDefault(c => c.Type == "iss")?.Value
                     ?? options.Oidc.Authority;
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
        {
            context.Fail("Token has no subject claim.");
            return null;
        }

        var email = principal.FindFirstValue(options.Oidc.EmailClaimType)
                    ?? principal.FindFirstValue(ClaimTypes.Email)
                    ?? $"{subject}@unknown";

        var externalSubject = $"{issuer.TrimEnd('/')}|{subject}";

        return await provisioner.EnsureProvisionedAsync(
            externalSubject,
            email,
            context.HttpContext.RequestAborted);
    }
}
