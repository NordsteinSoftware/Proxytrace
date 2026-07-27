using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Proxytrace.Api.Auth;

namespace Proxytrace.Api.Tests.Auth;

[TestClass]
public sealed class SessionCookieTests
{
    private static string AppendAndRead(bool secure)
    {
        var context = new DefaultHttpContext();
        // The backend hop is plain HTTP in the documented topology (TLS terminates at the reverse
        // proxy), so a request-derived Secure flag would always be false here.
        context.Request.Scheme = "http";
        var cookie = new SessionCookie(new SessionCookieOptions { Secure = secure });

        cookie.Append(context.Response, "session-jwt", DateTimeOffset.UtcNow.AddDays(7));

        return context.Response.Headers.SetCookie.ToString().ToLowerInvariant();
    }

    [TestMethod]
    public void Secure_ByDefault_IsEnabled()
    {
        new SessionCookieOptions().Secure.Should().BeTrue();
    }

    [TestMethod]
    public void Append_OverPlainHttpHop_StillMarksTheCookieSecure()
    {
        var setCookie = AppendAndRead(secure: true);

        setCookie.Should().Contain("proxytrace_session=session-jwt");
        setCookie.Should().Contain("; secure");
    }

    [TestMethod]
    public void Append_WhenSecureDisabled_OmitsTheSecureAttribute()
    {
        var setCookie = AppendAndRead(secure: false);

        setCookie.Should().Contain("proxytrace_session=session-jwt");
        setCookie.Should().NotContain("; secure");
    }

    [TestMethod]
    public void Append_Always_KeepsHttpOnlyAndStrictSameSite()
    {
        var setCookie = AppendAndRead(secure: true);

        setCookie.Should().Contain("httponly").And.Contain("samesite=strict");
    }

    [TestMethod]
    public void Delete_WhenSecureConfigured_ClearsTheCookieWithMatchingAttributes()
    {
        var context = new DefaultHttpContext();
        var cookie = new SessionCookie(new SessionCookieOptions { Secure = true });

        cookie.Delete(context.Response);

        var setCookie = context.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        setCookie.Should().Contain("proxytrace_session=;");
        setCookie.Should().Contain("; secure");
    }
}
