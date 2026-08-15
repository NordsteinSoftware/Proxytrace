using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local.Internal;

internal sealed class LocalTokenIssuer : ILocalTokenIssuer
{
    private readonly LocalAuthOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalTokenIssuer"/> class.
    /// </summary>
    public LocalTokenIssuer(LocalAuthOptions options)
    {
        this.options = options;
    }

    /// <summary>
    /// Issue.
    /// </summary>
    public LocalTokenResult Issue(IUser user)
    {
        var expires = DateTimeOffset.UtcNow + options.TokenLifetime;
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("email", user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            ],
            expires: expires.UtcDateTime,
            signingCredentials: creds);
        return new LocalTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
