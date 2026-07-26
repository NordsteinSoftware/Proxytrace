using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Common.Lifecycle;

namespace Proxytrace.Common.DependencyInjection;

public static class AutofacExtensions
{
    private const string PopulatedDescriptorsKey = "Proxytrace.ServiceCollection.PopulatedDescriptors";

    public static void RegisterServiceCollection(this ContainerBuilder builder, Action<IServiceCollection> config)
    {
        var services = new ServiceCollection();
        config(services);
        DropAlreadyPopulated(builder, services);
        builder.Populate(services);
    }

    /// <summary>
    /// Removes descriptors that an earlier <see cref="RegisterServiceCollection"/> call already
    /// populated into this container, so registering the same concrete implementation twice does not
    /// leave two copies behind.
    /// </summary>
    /// <remarks>
    /// Framework extension methods share their plumbing through <c>TryAdd</c>/<c>TryAddEnumerable</c>,
    /// which dedupes only within *one* <see cref="IServiceCollection"/>. Every call here builds a
    /// fresh collection, so each one re-adds that plumbing and <c>Populate</c> faithfully registers
    /// all of it. Four modules calling <c>AddHttpClient</c> therefore put four
    /// <c>IHttpMessageHandlerBuilderFilter</c>s in the container, and the logging handler each one
    /// contributes wrapped every outgoing request — so a single upstream LLM call was logged four
    /// times, on the hottest path in the system (#451).
    ///
    /// Only type-based registrations are compared: an identical (service, implementation, lifetime)
    /// triple can never mean two *different* things, whereas instance- and factory-based descriptors
    /// are opaque and are always populated as written. Genuine multi-registrations of one service
    /// (the point of <c>IEnumerable&lt;T&gt;</c> resolution) use distinct implementation types and are
    /// untouched.
    /// </remarks>
    private static void DropAlreadyPopulated(ContainerBuilder builder, IServiceCollection services)
    {
        if (!builder.Properties.TryGetValue(PopulatedDescriptorsKey, out object? stored)
            || stored is not HashSet<(Type Service, Type Implementation, ServiceLifetime Lifetime)> populated)
        {
            populated = [];
            builder.Properties[PopulatedDescriptorsKey] = populated;
        }

        for (int i = services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = services[i];

            // Keyed descriptors throw on the non-keyed accessors; they are rare and always explicit,
            // so leave them alone rather than reaching for their keyed counterparts.
            if (descriptor.IsKeyedService || descriptor.ImplementationType is not { } implementation)
            {
                continue;
            }

            if (!populated.Add((descriptor.ServiceType, implementation, descriptor.Lifetime)))
            {
                services.RemoveAt(i);
            }
        }
    }

    public static IReadOnlyCollection<Type> GetImplementations(
        this Type type, 
        Assembly? assembly = null)
    {
        assembly ??= type.Assembly;
        return assembly
            .GetTypes()
            .Where(t => type.IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
            .ToArray();
    }

    public static void OnDispose(this ContainerBuilder builder, Action action) 
        => builder.RegisterInstance(Disposable.Create(action));
}