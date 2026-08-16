using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Auth.Kiosk;

internal sealed class KioskAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// The scheme name constant value.
    /// </summary>
    public const string SchemeName = "Kiosk";

    private readonly IUserRepository users;
    private readonly KioskOptions kioskOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="KioskAuthenticationHandler"/> class.
    /// </summary>
    public KioskAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IUserRepository users,
        KioskOptions kioskOptions)
        : base(options, logger, encoder)
    {
        this.users = users;
        this.kioskOptions = kioskOptions;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = await users.FindByEmailAsync(kioskOptions.DemoUserEmail, Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("Kiosk demo user not seeded.");
        }

        Context.Items[CurrentUserAccessor.UserIdItemKey] = user.Id;

        var claims = new[]
        {
            new Claim("sub", user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
