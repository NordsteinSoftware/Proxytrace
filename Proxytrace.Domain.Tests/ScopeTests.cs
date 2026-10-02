using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Scope;
using Proxytrace.Domain.Session;

namespace Proxytrace.Domain.Tests;

[TestClass]
public class ScopeTests : DomainTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithCanonicalKey_PersistsAndReloads()
    {
        var services = GetServices();
        var project = await GetOrCreate<IProject>(services);
        var factory = services.GetRequiredService<IScope.CreateNew>();

        var scope = await factory("support-agents", project.Id, "Support agents", "Tier-1 triage").AddAsync(CancellationToken);

        var loaded = await services.GetRequiredService<IRepository<IScope>>().GetAsync(scope.Id, CancellationToken);
        loaded.ExternalKey.Should().Be("support-agents");
        loaded.ProjectId.Should().Be(project.Id);
        loaded.DisplayName.Should().Be("Support agents");
        loaded.Description.Should().Be("Tier-1 triage");
    }

    [TestMethod]
    public async Task CreateNew_WithNonCanonicalKey_FailsValidation()
    {
        var services = GetServices();
        var project = await GetOrCreate<IProject>(services);
        var factory = services.GetRequiredService<IScope.CreateNew>();

        // The key is the URL segment: storing a raw "Support_Agents" would make the same scope
        // reachable under two spellings.
        await FluentActions
            .Invoking(() => factory("Support_Agents", project.Id).AddAsync(CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithDefaultProject_FailsValidation()
    {
        var services = GetServices();
        var factory = services.GetRequiredService<IScope.CreateNew>();

        await FluentActions
            .Invoking(() => factory("support", Guid.Empty).AddAsync(CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task ChangeDetails_WithNewValues_PersistsTrimmedValues()
    {
        var services = GetServices();
        var scope = await GetOrCreate<IScope>(services);

        await scope.ChangeDetails("  Billing  ", "Invoices and refunds", CancellationToken);

        var loaded = await services.GetRequiredService<IRepository<IScope>>().GetAsync(scope.Id, CancellationToken);
        loaded.DisplayName.Should().Be("Billing");
        loaded.Description.Should().Be("Invoices and refunds");
        loaded.ExternalKey.Should().Be(scope.ExternalKey, "the key is the URL segment and never changes");
    }

    [TestMethod]
    public async Task ChangeDetails_WithBlankValues_ClearsFields()
    {
        var services = GetServices();
        var scope = await GetOrCreate<IScope>(services);
        scope = await scope.ChangeDetails("Billing", "Invoices", CancellationToken);

        var cleared = await scope.ChangeDetails("   ", null, CancellationToken);

        cleared.DisplayName.Should().BeNull();
        cleared.Description.Should().BeNull();
    }

    [TestMethod]
    public async Task ChangeDetails_WithTooLongDisplayName_Throws()
    {
        var services = GetServices();
        var scope = await GetOrCreate<IScope>(services);

        await FluentActions
            .Invoking(() => scope.ChangeDetails(new string('x', IScope.MaxDisplayNameLength + 1), null, CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    [DataRow("support-agents", "support-agents")]
    [DataRow("Support Agents", "support-agents")]
    [DataRow("support_agents", "support-agents")]
    [DataRow("  --Support__Agents--  ", "support-agents")]
    [DataRow("Kundenservice Überweisung", "kundenservice-überweisung")]
    [DataRow("v2!", "v2")]
    public void ScopeKeyNormalize_WithUsableInput_ReturnsCanonicalSlug(string raw, string expected)
        => ScopeKey.Normalize(raw).Should().Be(expected);

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("!!!")]
    [DataRow("openai")]
    [DataRow("OpenAI")]
    [DataRow("v1")]
    public void ScopeKeyNormalize_WithUnusableOrReservedInput_ReturnsNull(string? raw)
        => ScopeKey.Normalize(raw).Should().BeNull();

    [TestMethod]
    public void ScopeKeyNormalize_WithOverlongInput_TruncatesWithoutTrailingHyphen()
    {
        // 63 letters then a separator: a naive cut at 64 would end the key on "-".
        var raw = new string('a', ScopeKey.MaxLength - 1) + " tail";

        var key = ScopeKey.Normalize(raw);

        key.Should().Be(new string('a', ScopeKey.MaxLength - 1));
    }

    [TestMethod]
    [DataRow("Support Agents")]
    [DataRow("a-b_c d")]
    [DataRow("Kundenservice Überweisung")]
    public void ScopeKeyNormalize_AppliedTwice_IsIdempotent(string raw)
    {
        var once = ScopeKey.Normalize(raw);

        ScopeKey.Normalize(once).Should().Be(once);
    }

    [TestMethod]
    public void ScopeIdDerivation_SameInputs_IsDeterministic()
    {
        var projectId = Guid.NewGuid();

        ScopeIdDerivation.Derive(projectId, "support")
            .Should().Be(ScopeIdDerivation.Derive(projectId, "support"));
    }

    [TestMethod]
    public void ScopeIdDerivation_DifferentProjects_DifferentIds()
        => ScopeIdDerivation.Derive(Guid.NewGuid(), "support")
            .Should().NotBe(ScopeIdDerivation.Derive(Guid.NewGuid(), "support"));

    [TestMethod]
    public void ScopeIdDerivation_SameKeyAsSession_DiffersFromSessionId()
    {
        var projectId = Guid.NewGuid();

        ScopeIdDerivation.Derive(projectId, "support")
            .Should().NotBe(SessionIdDerivation.Derive(projectId, "support"));
    }
}
