using System.Reflection;
using AwesomeAssertions;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

/// <summary>
/// Keeps deterministic test-data randomness away from anything security-relevant.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IRandom"/> is registered as <c>SeededRandom</c> with a <b>fixed seed</b>, so every
/// process produces the identical sequence. That is the point for reproducible fixtures and demo
/// data, and it is catastrophic for a credential: a secret drawn from it would be the same on every
/// installation in the world.
/// </para>
/// <para>
/// Nothing is broken today — every real credential path (API keys, invite and password-reset tokens,
/// TOTP secrets, MFA backup codes, MFA challenges, stream tickets) calls
/// <c>RandomNumberGenerator</c> directly, and no production code path resolves a
/// <c>IDomainEntityGenerator&lt;T&gt;</c>. But nothing structurally stopped a future caller from
/// injecting <see cref="IRandom"/> into a service that mints something secret, and the two kinds of
/// randomness are easy to confuse behind one interface name. This test is that structural stop.
/// </para>
/// </remarks>
[TestClass]
public sealed class SeededRandomIsNotUsedForSecretsTests : BaseTest<Module>
{
    /// <summary>
    /// Production types allowed to depend on <see cref="IRandom"/> despite not being a domain
    /// generator. Each entry needs a reason, and must not mint anything a caller authenticates with.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Allowed = new Dictionary<string, string>
    {
        // Seeds the demo/kiosk dataset. Deterministic on purpose: the showcase must look the same
        // on every boot. Produces trace statistics, never a credential.
        ["Proxytrace.Application.Demo.Scenarios.StatisticsBackfillScenario"] =
            "demo data seeding — deterministic by design, mints no credential",
    };

    [TestMethod]
    public void NoProductionTypeOutsideTheGeneratorSurfaceDependsOnSeededRandom()
    {
        var offenders = ProductionAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(TakesRandomDependency)
            .Where(t => !IsDomainGenerator(t))
            .Where(t => t.FullName is null || !Allowed.ContainsKey(t.FullName))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "IRandom is a fixed-seed generator for test and demo data. A secret drawn from it would "
            + "be identical on every installation. Use RandomNumberGenerator for anything a caller "
            + "authenticates with; if this type genuinely needs reproducible non-secret randomness, "
            + "add it to the allowlist above with a reason.");
    }

    [TestMethod]
    public void TheGuardCanActuallySeeTheTypesItIsMeantToProtect()
    {
        // A reflection guard that silently scans nothing always passes. Assert the scan reaches the
        // real graph and does find IRandom consumers among the generators.
        var generatorsUsingRandom = ProductionAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(TakesRandomDependency)
            .Where(IsDomainGenerator)
            .ToArray();

        generatorsUsingRandom.Should().NotBeEmpty(
            "the domain test-data generators take IRandom — if none are found the scan is not "
            + "reaching the production assemblies and the guard above proves nothing");
    }

    private static IEnumerable<Assembly> ProductionAssemblies()
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name is { } name
                        && name.StartsWith("Proxytrace.", StringComparison.Ordinal)
                        && !name.EndsWith(".Tests", StringComparison.Ordinal)
                        // The testing harness exists to build fixtures; it is not production code.
                        && name != "Nordstein.Core.Testing");

    private static bool TakesRandomDependency(Type type)
        => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IRandom)));

    private static bool IsDomainGenerator(Type type)
        => type.GetInterfaces().Any(i => i.IsGenericType
            && (i.GetGenericTypeDefinition() == typeof(IDomainEntityGenerator<>)
                || i.GetGenericTypeDefinition() == typeof(IDomainObjectGenerator<>)));
}
