using Autofac;
using Proxytrace.Application.TestCase.Internal;

namespace Proxytrace.Application.TestCase;

internal sealed class TestCaseModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        // Per-scope, not singleton: it resolves IAgentCallRepository / IAgentRepository, which are
        // bound to the request's ambient DbContext. A singleton would capture one and leak it.
        builder.RegisterType<TestCaseSynthesisService>()
            .As<ITestCaseSynthesisService>()
            .InstancePerLifetimeScope();
    }
}
