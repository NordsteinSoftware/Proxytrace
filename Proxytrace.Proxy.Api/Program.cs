using System.Text.Json;
using System.Text.Json.Serialization;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Proxytrace.Proxy.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Kestrel's default MaxRequestBodySize is 30 MB, which would reject an oversized request before the
// controller's own 64 MiB cap ever ran — leaving OpenAiProxyController.MaxRequestBodyBytes (and the
// 413 it produces) unreachable, and the real ceiling silently half the documented one. Pin the server
// limit to the same constant so there is exactly one request-size bound.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = OpenAiProxyController.MaxRequestBodyBytes);

builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
    containerBuilder.RegisterModule<Proxytrace.Proxy.Api.Module>());

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .AddApplicationPart(typeof(OpenAiProxyController).Assembly);

var app = builder.Build();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
