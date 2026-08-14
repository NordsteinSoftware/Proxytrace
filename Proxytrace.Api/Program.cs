using Autofac;
using Autofac.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Proxytrace.Api;
using Proxytrace.Api.Auth.Mcp;
using Proxytrace.Api.Kiosk;
using Proxytrace.Api.Middleware;
using Nordstein.Core.Common.Hosting;
using Proxytrace.Domain.Kiosk;
using Module = Proxytrace.Api.Module;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
    containerBuilder.RegisterModule<Module>());

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy
            .WithOrigins(builder.Configuration["Frontend:AllowedOrigin"] ?? "http://localhost:4201")
            .AllowAnyMethod()
            .AllowAnyHeader());
});

builder.Services.AddHttpContextAccessor();

// A throwing BackgroundService must not take the API down (see the extension's remarks and #522):
// .NET's default stops the host and exits 0, which no restart policy treats as a failure.
builder.Services.AddResilientBackgroundServices();

// Throttle the anonymous auth endpoints (login/signup, password reset, MFA verify) per client IP.
// In-memory is fine — each deployment runs a single API instance. Applied via
// [EnableRateLimiting(...)] on the endpoints. NOTE: the partition key is the *connection* remote
// address, which is only the real client when UseForwardedHeaders below is configured to trust the
// reverse proxy — otherwise every request shares one bucket. See docs/security.md.
var rateLimiting = new AuthRateLimiterConfigurator(builder.Configuration);
builder.Services.AddRateLimiter(rateLimiting.Configure);

builder.Services.AddAuthorization(options =>
{
    // The MCP endpoint authenticates only via the McpApiKey scheme: a browser JWT/cookie must not
    // reach it, and an MCP API key is not valid for the rest of the API. The policy is harmless when
    // unused — it is only ever evaluated if the /mcp endpoint is mapped (non-kiosk).
    options.AddPolicy("Mcp", policy => policy
        .AddAuthenticationSchemes(McpApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser());
});

builder.Services.AddControllers(options =>
        options.Filters.Add<Proxytrace.Api.Auth.Licensing.LicenseEnforcementFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Proxytrace API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter the token below, without any prefixes",
        });
        c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", doc)] = [],
        });
    });
}

#if DEBUG
// DEBUG-ONLY developer back-door: seed a fixed admin (debug@proxytrace.dev) so a local debug build
// can always sign in through the normal login form. Compiled out of Release entirely — both this
// registration and the seeder type are under #if DEBUG. See Proxytrace.Api/Debug + docs/debug_api.md.
builder.Services.AddHostedService<Proxytrace.Api.Debug.DebugLoginSeederHostedService>();
#endif

var app = builder.Build();

// Resolve the kiosk decision from the container (the Module is the single source of truth — it reads
// appsettings.local.json, which builder.Configuration does not). Re-reading config here would diverge.
var kioskEnabled = app.Services.GetRequiredService<KioskOptions>().Enabled;

// Kiosk showcase: mount the OpenAI-compatible proxy controller in-process ONLY when kiosk runs with a
// live Kiosk:Endpoint (the same gate Proxytrace.Api.Module uses to register Proxytrace.Proxy.Module).
// The Web SDK auto-generates an ApplicationPart for the referenced Proxytrace.Proxy library (it holds
// MVC controllers), so the controller would otherwise be mounted in EVERY mode. KioskProxyMounting.Apply
// takes control of that auto-added part — keeping it only in kiosk+endpoint mode and stripping it
// otherwise so `openai/v1/{**path}` stays absent (404) in production and kiosk-without-endpoint. Applied
// before MapControllers so the action descriptors reflect the decision. See docs/architecture.md and
// Proxytrace.Api/Kiosk/KioskProxyMounting.cs.
var mountKioskProxy = kioskEnabled
                      && app.Services.GetRequiredService<KioskEndpointOptions>().IsConfigured;
KioskProxyMounting.Apply(app.Services.GetRequiredService<ApplicationPartManager>(), mountKioskProxy);

// FIRST middleware: rewrite Request.Scheme / Connection.RemoteIpAddress from the X-Forwarded-Proto
// and X-Forwarded-For headers the reverse proxy sets, so everything downstream (rate-limit partition
// keys, audit trails, generated absolute URLs) sees the real client instead of the proxy container.
// Only headers arriving from an explicitly trusted proxy are honoured — an unrestricted
// X-Forwarded-For would be a client-controlled rate-limit partition key. Unconfigured, the trust set
// is the framework default (loopback only), so this is a no-op behind a containerised proxy until an
// operator declares it. See docs/security.md.
var forwardedHeaders = new TrustedProxyConfiguration(builder.Configuration).Build();
if (forwardedHeaders is not null)
{
    app.UseForwardedHeaders(forwardedHeaders);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Proxytrace API v1"));
}

app.UseCors("Frontend");
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<KioskReadOnlyMiddleware>();
// Before UseAuthorization so it still runs when an authorization failure short-circuits with 403.
app.UseMiddleware<AuditDeniedAccessMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

// MCP server (Streamable HTTP, stateless). Authenticated per-request by an API key whose project
// scopes the call (see McpApiKeyAuthenticationHandler + IMcpProjectAccessor). Omitted in kiosk.
if (!kioskEnabled)
{
    app.MapMcp("/mcp").RequireAuthorization("Mcp");
}
// The bundled VitePress manual lives under wwwroot/docs. Existing files (e.g.
// /docs/guide/x.html) are served by the static middleware above. A bare directory
// request like /docs or /docs/ has no file to match and UseDefaultFiles is skipped
// once an endpoint is selected, so route any unmatched /docs path to the manual's
// index instead of letting it fall through to the SPA fallback below.
app.MapFallbackToFile("/docs/{*path}", "docs/index.html");
app.MapFallbackToFile("index.html");

app.Run();

namespace Proxytrace.Api
{
    /// <summary>
    /// Builds the forwarded-headers configuration from the <c>ForwardedHeaders</c> config section.
    /// The documented topology terminates TLS at a reverse proxy and forwards plain HTTP, so the API
    /// only learns the real client address and scheme from <c>X-Forwarded-For</c> /
    /// <c>X-Forwarded-Proto</c>. Those headers are attacker-controlled unless the peer that sent them
    /// is trusted, so the trust set must be declared by the operator: with none declared the
    /// framework default (loopback only) applies and forwarded headers from a containerised proxy are
    /// ignored — the fail-safe choice, since a spoofable client address would silently defeat the
    /// per-IP auth rate limiters. See docs/security.md.
    /// </summary>
    internal sealed class TrustedProxyConfiguration
    {
        public const string SectionName = "ForwardedHeaders";

        private readonly IConfiguration configuration;

        public TrustedProxyConfiguration(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        /// <summary>
        /// Returns the options to hand to <c>UseForwardedHeaders</c>, or <see langword="null"/> when
        /// the operator disabled forwarded-header processing entirely.
        /// </summary>
        public ForwardedHeadersOptions? Build()
        {
            var section = configuration.GetSection(SectionName);
            if (section.GetValue<bool?>("Enabled") == false)
            {
                return null;
            }

            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                                   | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,

                // One hop by default: the single reverse proxy of the documented topology. Raise it
                // only when there really are N chained proxies, all of them trusted.
                ForwardLimit = section.GetValue<int?>("ForwardLimit") ?? 1,
            };

            foreach (var proxy in ReadList(section, "KnownProxies"))
            {
                options.KnownProxies.Add(ParseAddress(proxy));
            }

            foreach (var network in ReadList(section, "KnownNetworks"))
            {
                options.KnownIPNetworks.Add(ParseNetwork(network));
            }

            return options;
        }

        // Accepts both the array form (ForwardedHeaders:KnownNetworks:0) and a comma-separated
        // scalar, so a container can declare the trust set in a single environment variable.
        private IReadOnlyList<string> ReadList(IConfigurationSection section, string key)
        {
            var child = section.GetSection(key);
            if (child.Value is not null)
            {
                return child.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            return child.Get<string[]>() ?? [];
        }

        private IPAddress ParseAddress(string value) =>
            IPAddress.TryParse(value, out var address)
                ? address
                : throw new InvalidOperationException(
                    $"{SectionName}:KnownProxies contains '{value}', which is not a valid IP address.");

        private System.Net.IPNetwork ParseNetwork(string value) =>
            System.Net.IPNetwork.TryParse(value, out var network)
                ? network
                : throw new InvalidOperationException(
                    $"{SectionName}:KnownNetworks contains '{value}', which is not valid CIDR notation (e.g. 172.16.0.0/12).");
    }

    /// <summary>
    /// Registers the fixed-window rate-limiting policies guarding the anonymous auth endpoints, with
    /// operator-overridable limits under the <c>RateLimiting</c> config section.
    /// </summary>
    internal sealed class AuthRateLimiterConfigurator
    {
        public const string SectionName = "RateLimiting";

        /// <summary>
        /// Guards the anonymous credential-accepting endpoints: login, legacy claim, invite signup
        /// and the invite-token preview.
        /// </summary>
        public const string LoginPolicy = "auth-login";

        public const string PasswordResetPolicy = "auth-reset";
        public const string MfaPolicy = "auth-mfa";

        private readonly IConfiguration configuration;

        public AuthRateLimiterConfigurator(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public void Configure(RateLimiterOptions options)
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddPolicy(options, LoginPolicy, LoginLimits());
            AddPolicy(options, PasswordResetPolicy, PasswordResetLimits());
            AddPolicy(options, MfaPolicy, MfaLimits());
        }

        /// <summary>
        /// Password guessing is otherwise bounded only by throughput (there is no per-account failed
        /// attempt counter), so a client address gets 30 credential submissions per minute: an order
        /// of magnitude above a human fumbling a password or a shared office NAT signing in, and
        /// three-plus orders of magnitude below what an unthrottled endpoint allows.
        /// </summary>
        public FixedWindowRateLimiterOptions LoginLimits() => Limits("Login", 30, TimeSpan.FromMinutes(1));

        /// <summary>
        /// Blunts account enumeration and brute-forcing of reset tokens.
        /// </summary>
        public FixedWindowRateLimiterOptions PasswordResetLimits() =>
            Limits("PasswordReset", 10, TimeSpan.FromMinutes(15));

        /// <summary>
        /// The MFA verify endpoint validates a 6-digit code — a small space — so it is limited in
        /// addition to the per-challenge attempt cap.
        /// </summary>
        public FixedWindowRateLimiterOptions MfaLimits() => Limits("Mfa", 10, TimeSpan.FromMinutes(15));

        /// <summary>
        /// The partition a request is counted against. Only as good as the forwarded-headers trust
        /// configuration: behind an untrusted proxy every client collapses into one bucket.
        /// </summary>
        public string PartitionKey(HttpContext httpContext) =>
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        private void AddPolicy(RateLimiterOptions options, string policy, FixedWindowRateLimiterOptions limits) =>
            options.AddPolicy(policy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKey(httpContext),
                    factory: _ => limits));

        private FixedWindowRateLimiterOptions Limits(string key, int permitLimit, TimeSpan window)
        {
            var section = configuration.GetSection($"{SectionName}:{key}");
            var seconds = section.GetValue<int?>("WindowSeconds");
            return new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, section.GetValue<int?>("PermitLimit") ?? permitLimit),
                Window = seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : window,
                QueueLimit = 0,
            };
        }
    }
}
