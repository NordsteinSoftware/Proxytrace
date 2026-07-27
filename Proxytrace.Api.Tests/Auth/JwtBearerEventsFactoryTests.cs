using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Application.Auth;
using Proxytrace.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Tests.Auth;

[TestClass]
public sealed class JwtBearerEventsFactoryTests
{
    private sealed class StubAuthUserResolver : IAuthUserResolver
    {
        private readonly IUser? user;

        public StubAuthUserResolver(IUser? user)
        {
            this.user = user;
        }

        public Task<IUser?> Resolve(TokenValidatedContext context, ClaimsPrincipal principal)
            => Task.FromResult(user);
    }

    private static AuthenticationScheme Scheme() => new(
        JwtBearerDefaults.AuthenticationScheme,
        JwtBearerDefaults.AuthenticationScheme,
        typeof(JwtBearerHandler));

    [TestMethod]
    public async Task OnMessageReceived_WithAccessTokenQuery_OnStreamRoute_SetsToken()
    {
        var events = JwtBearerEventsFactory.Create();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/agents/123/proposals/stream";
        httpContext.Request.QueryString = new QueryString("?access_token=abc123");
        var ctx = new MessageReceivedContext(httpContext, Scheme(), new JwtBearerOptions());

        await events.OnMessageReceived(ctx);

        ctx.Token.Should().Be("abc123");
    }

    [TestMethod]
    public async Task OnMessageReceived_WithAccessTokenQuery_OnNonStreamRoute_IsIgnored()
    {
        // The session JWT must not be honored from the URL on ordinary requests — only on SSE
        // (GET …/stream) routes where the EventSource API cannot set headers.
        var events = JwtBearerEventsFactory.Create();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/agents";
        httpContext.Request.QueryString = new QueryString("?access_token=abc123");
        var ctx = new MessageReceivedContext(httpContext, Scheme(), new JwtBearerOptions());

        await events.OnMessageReceived(ctx);

        ctx.Token.Should().BeNull();
    }

    [TestMethod]
    public async Task OnMessageReceived_WithValidStreamTicket_AuthenticatesUser()
    {
        var userId = Guid.NewGuid();
        var user = Substitute.For<IUser>();
        user.Id.Returns(userId);
        user.Role.Returns(UserRole.Admin);

        var tickets = Substitute.For<IStreamTicketService>();
        tickets.Consume("ticket-abc").Returns(userId);
        var repo = Substitute.For<IRepository<IUser>>();
        repo.FindAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(tickets);
        serviceCollection.AddSingleton(repo);
        var httpContext = new DefaultHttpContext { RequestServices = serviceCollection.BuildServiceProvider() };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/agent-calls/stream";
        httpContext.Request.QueryString = new QueryString("?stream_ticket=ticket-abc");
        var ctx = new MessageReceivedContext(httpContext, Scheme(), new JwtBearerOptions());

        var events = JwtBearerEventsFactory.Create();

        await events.OnMessageReceived(ctx);

        ctx.Result?.Succeeded.Should().BeTrue();
        ctx.Principal.Should().NotBeNull();
        ctx.Principal.IsInRole(nameof(UserRole.Admin)).Should().BeTrue();
        httpContext.Items[CurrentUserAccessor.UserIdItemKey].Should().Be(userId);
    }

    [TestMethod]
    public async Task OnMessageReceived_WithStreamTicketOnNonStreamRoute_DoesNotAuthenticate()
    {
        // The ticket redeems into a full principal including the live Role claim, so honoring it
        // outside the SSE routes it was minted for would turn a URL-borne credential into general
        // API access — including admin endpoints — for its validity window.
        var userId = Guid.NewGuid();
        var user = Substitute.For<IUser>();
        user.Id.Returns(userId);
        user.Role.Returns(UserRole.Admin);

        var tickets = Substitute.For<IStreamTicketService>();
        tickets.Consume("ticket-abc").Returns(userId);
        var repo = Substitute.For<IRepository<IUser>>();
        repo.FindAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(tickets);
        serviceCollection.AddSingleton(repo);
        var httpContext = new DefaultHttpContext { RequestServices = serviceCollection.BuildServiceProvider() };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/users";
        httpContext.Request.QueryString = new QueryString("?stream_ticket=ticket-abc");
        var ctx = new MessageReceivedContext(httpContext, Scheme(), new JwtBearerOptions());

        var events = JwtBearerEventsFactory.Create();

        await events.OnMessageReceived(ctx);

        ctx.Result.Should().BeNull();
        ctx.Principal.Should().BeNull();
        httpContext.Items.Should().NotContainKey(CurrentUserAccessor.UserIdItemKey);
        // The ticket must also stay unspent, so a genuine stream request can still redeem it.
        tickets.DidNotReceive().Consume(Arg.Any<string>());
    }

    [TestMethod]
    public async Task OnMessageReceived_WithInvalidStreamTicket_FailsContext()
    {
        var tickets = Substitute.For<IStreamTicketService>();
        tickets.Consume(Arg.Any<string>()).Returns((Guid?)null);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(tickets);
        serviceCollection.AddSingleton(Substitute.For<IRepository<IUser>>());
        var httpContext = new DefaultHttpContext { RequestServices = serviceCollection.BuildServiceProvider() };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/agent-calls/stream";
        httpContext.Request.QueryString = new QueryString("?stream_ticket=bad");
        var ctx = new MessageReceivedContext(httpContext, Scheme(), new JwtBearerOptions());

        var events = JwtBearerEventsFactory.Create();

        await events.OnMessageReceived(ctx);

        ctx.Result?.Failure.Should().NotBeNull();
    }

    [TestMethod]
    public async Task OnMessageReceived_WithSessionCookie_SetsToken()
    {
        var events = JwtBearerEventsFactory.Create();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{SessionCookie.Name}=cookie-jwt";
        var ctx = new MessageReceivedContext(httpContext, Scheme(), new JwtBearerOptions());

        await events.OnMessageReceived(ctx);

        ctx.Token.Should().Be("cookie-jwt");
    }

    [TestMethod]
    public async Task OnMessageReceived_WithAuthorizationHeaderAndCookie_LeavesTokenForHeader()
    {
        // The handler parses the Authorization header after this event when Token is null —
        // the cookie must not pre-empt an explicit bearer credential.
        var events = JwtBearerEventsFactory.Create();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer header-jwt";
        httpContext.Request.Headers.Cookie = $"{SessionCookie.Name}=cookie-jwt";
        var ctx = new MessageReceivedContext(httpContext, Scheme(), new JwtBearerOptions());

        await events.OnMessageReceived(ctx);

        ctx.Token.Should().BeNull();
    }

    [TestMethod]
    public async Task OnMessageReceived_WithoutQuery_LeavesTokenNull()
    {
        var events = JwtBearerEventsFactory.Create();
        var ctx = new MessageReceivedContext(new DefaultHttpContext(), Scheme(), new JwtBearerOptions());

        await events.OnMessageReceived(ctx);

        ctx.Token.Should().BeNull();
    }

    [TestMethod]
    public async Task OnTokenValidated_NullPrincipal_FailsContext()
    {
        var events = JwtBearerEventsFactory.Create();
        var ctx = new TokenValidatedContext(new DefaultHttpContext(), Scheme(), new JwtBearerOptions())
        {
            Principal = null,
        };

        await events.OnTokenValidated(ctx);

        ctx.Result?.Failure.Should().NotBeNull();
    }

    [TestMethod]
    public async Task OnTokenValidated_ResolvedUser_StoresUserIdAndAddsRoleClaim()
    {
        var userId = Guid.NewGuid();
        var user = Substitute.For<IUser>();
        user.Id.Returns(userId);
        user.Role.Returns(UserRole.Admin);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IAuthUserResolver>(new StubAuthUserResolver(user));
        var httpContext = new DefaultHttpContext { RequestServices = serviceCollection.BuildServiceProvider() };

        var identity = new ClaimsIdentity([new Claim("sub", userId.ToString())], "Test");
        var ctx = new TokenValidatedContext(httpContext, Scheme(), new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(identity),
        };

        var events = JwtBearerEventsFactory.Create();

        await events.OnTokenValidated(ctx);

        httpContext.Items[CurrentUserAccessor.UserIdItemKey].Should().Be(userId);
        identity.HasClaim(ClaimTypes.Role, nameof(UserRole.Admin)).Should().BeTrue();
    }

    [TestMethod]
    public async Task OnTokenValidated_TokenRoleDiffersFromLiveRole_UsesLiveRole()
    {
        // A demoted user still holds a 7-day token that bakes the old Admin role. The live DB
        // role (Member) must win, and the stale Admin claim must not survive — otherwise the
        // demotion has no effect until the token expires.
        var userId = Guid.NewGuid();
        var user = Substitute.For<IUser>();
        user.Id.Returns(userId);
        user.Role.Returns(UserRole.Member);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IAuthUserResolver>(new StubAuthUserResolver(user));
        var httpContext = new DefaultHttpContext { RequestServices = serviceCollection.BuildServiceProvider() };

        var identity = new ClaimsIdentity(
            [new Claim("sub", userId.ToString()), new Claim(ClaimTypes.Role, nameof(UserRole.Admin))],
            "Test");
        var ctx = new TokenValidatedContext(httpContext, Scheme(), new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(identity),
        };

        var events = JwtBearerEventsFactory.Create();

        await events.OnTokenValidated(ctx);

        identity.FindAll(ClaimTypes.Role).Should().ContainSingle()
            .Which.Value.Should().Be(nameof(UserRole.Member));
        identity.HasClaim(ClaimTypes.Role, nameof(UserRole.Admin)).Should().BeFalse();
    }
}
