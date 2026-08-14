using Nordstein.Core.AI.Clients;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Storage;
using Nordstein.Core.Testing;

namespace Proxytrace.Benchmarks;

/// <summary>
/// Builds a lightweight in-memory container purely to mint realistic sample domain objects and to
/// resolve the production <see cref="ISerializer"/>. Nothing here is timed — the container exists only
/// so the benchmarks measure the real serializer against real generated payloads, not hand-rolled stubs.
/// </summary>
internal static class BenchFixture
{
    public static IContainer Build()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new BenchModule());
        return builder.Build();
    }

    private sealed class BenchModule : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            base.Load(builder);
            builder
                .Register(sp => new AutofacServiceProvider(sp.Resolve<ILifetimeScope>()))
                .As<IServiceProvider>();
            builder.RegisterStub<IHostEnvironment>(env => env.ContentRootPath.Returns(Path.GetTempPath()));
            builder.RegisterServiceCollection(sc => sc.AddLogging());
            builder.RegisterServiceCollection(sc => sc.AddDataProtection());

            // Domain generators + the serializer come from Storage.Module's transitive Domain/Common
            // modules; Application services aren't needed to mint value objects, so skip them.
            builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.InMemory(), registerApplicationServices: false));
            builder.RegisterStub<IModelClient>();
            builder.RegisterStub<IProviderClient>();
        }
    }

    public sealed record Samples(
        ISerializer Serializer,
        Conversation Conversation,
        string ConversationJson,
        AssistantMessage Response,
        string ResponseJson);

    public static async Task<Samples> CreateSamplesAsync(IContainer container)
    {
        await using var scope = container.BeginLifetimeScope();
        var serializer = scope.Resolve<ISerializer>();
        var conversationGenerator = scope.Resolve<IDomainObjectGenerator<Conversation>>();
        var completionGenerator = scope.Resolve<IDomainObjectGenerator<ICompletion>>();

        var conversation = await conversationGenerator.CreateAsync();
        var response = (await completionGenerator.CreateAsync()).Response;

        return new Samples(
            serializer,
            conversation,
            serializer.Serialize(conversation),
            response,
            serializer.Serialize(response));
    }
}
