using Proxytrace.Domain.AuditLog;
using Autofac;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.ApiKeys;
using Proxytrace.Api.Dto.ModelProviders;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Pricing;
using Nordstein.Core.Common.Net;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.User;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class ModelProvidersControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Create_PersistsProvider()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Create(
            new CreateModelProviderRequest("Acme", "https://api.acme.test/", "sk-test", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        var created = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException());
        var dto = (ModelProviderDto?)created.Value;
        dto.Should().NotBeNull();
        dto.Name.Should().Be("Acme");
        dto.Kind.Should().Be(ModelProviderKind.OpenAiCompatible);
    }

    [TestMethod]
    public async Task Create_SchemelessEndpoint_DefaultsToHttps()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Create(
            new CreateModelProviderRequest("Acme", "api.acme.test/v1", "sk-test", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        var created = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException());
        var dto = (ModelProviderDto?)created.Value;
        dto.Should().NotBeNull();
        dto.Endpoint.Should().Be("https://api.acme.test/v1");
    }

    [TestMethod]
    public async Task Create_MalformedEndpoint_ThrowsMalformedEndpointUrl()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        await FluentActions
            .Invoking(() => controller.Create(
                new CreateModelProviderRequest("Acme", "://not a url", "sk-test", ModelProviderKind.OpenAiCompatible),
                CancellationToken))
            .Should().ThrowAsync<MalformedEndpointUrlException>();
    }

    [TestMethod]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Update_Existing_PersistsChange()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        var result = await controller.Update(
            provider.Id,
            new UpdateModelProviderRequest("Renamed", provider.Endpoint.ToString(), provider.ApiKey, provider.Kind),
            CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Name.Should().Be("Renamed");
    }

    [TestMethod]
    public async Task GetAll_DoesNotReturnTheUpstreamKey()
    {
        // The key used to be projected into the list response, so every provider credential in the
        // installation reached the browser on every admin providers-page load.
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        var paged = await controller.GetAll(cancellationToken: CancellationToken);

        var dto = paged.Items.Should().ContainSingle(p => p.Id == provider.Id).Subject;
        provider.ApiKey.Should().NotBeNullOrEmpty("the fixture must have a key for this to be meaningful");
        dto.UpstreamApiKeyPreview.Should().NotBe(provider.ApiKey);
        dto.UpstreamApiKeyPreview.Should().Contain("•");
    }

    [TestMethod]
    public async Task GetOverview_DoesNotReturnTheUpstreamKey()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        var overview = await controller.GetOverview(CancellationToken);

        var dto = overview.Providers.Should().ContainSingle(p => p.Provider.Id == provider.Id).Subject;
        dto.Provider.UpstreamApiKeyPreview.Should().NotBe(provider.ApiKey);
    }

    [TestMethod]
    public async Task GetUpstreamKey_ReturnsTheKeyAndAuditsTheRead()
    {
        IServiceProvider services = GetServices();
        var audit = new RecordingAuditLogger();
        var controller = ResolveController(services, audit: audit);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        var result = await controller.GetUpstreamKey(provider.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.UpstreamApiKey.Should().Be(provider.ApiKey);
        audit.Events.Should().ContainSingle()
            .Which.Id.Should().Be((int)AuditAction.ProviderUpstreamKeyRevealed);
    }

    [TestMethod]
    public async Task GetUpstreamKey_UnknownProvider_ReturnsNotFoundAndAuditsNothing()
    {
        IServiceProvider services = GetServices();
        var audit = new RecordingAuditLogger();
        var controller = ResolveController(services, audit: audit);

        var result = await controller.GetUpstreamKey(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
        audit.Events.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Update_WithNullUpstreamKey_LeavesTheStoredCredentialUnchanged()
    {
        // The client no longer holds the key, so an edit that only renames a provider sends null.
        // Treating that as "no key" would wipe the credential and break every proxied call.
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var providers = services.GetRequiredService<IModelProviderRepository>();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var originalKey = provider.ApiKey;

        await controller.Update(
            provider.Id,
            new UpdateModelProviderRequest("Renamed", provider.Endpoint.ToString(), null, provider.Kind),
            CancellationToken);

        var reloaded = await providers.GetAsync(provider.Id, CancellationToken);
        reloaded.Name.Should().Be("Renamed");
        reloaded.ApiKey.Should().Be(originalKey);
    }

    [TestMethod]
    public async Task Update_WithNullUpstreamKey_DoesNotAuditAKeyRotation()
    {
        IServiceProvider services = GetServices();
        var audit = new RecordingAuditLogger();
        var controller = ResolveController(services, audit: audit);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        await controller.Update(
            provider.Id,
            new UpdateModelProviderRequest("Renamed", provider.Endpoint.ToString(), null, provider.Kind),
            CancellationToken);

        audit.Events.Should().ContainSingle()
            .Which.Id.Should().Be((int)AuditAction.ProviderConfigUpdated);
    }

    [TestMethod]
    public async Task Update_ChangedUpstreamKey_AuditsKeyRotation()
    {
        IServiceProvider services = GetServices();
        var audit = new RecordingAuditLogger();
        var controller = ResolveController(services, audit: audit);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        await controller.Update(
            provider.Id,
            new UpdateModelProviderRequest(provider.Name, provider.Endpoint.ToString(), "sk-rotated", provider.Kind),
            CancellationToken);

        audit.Events.Should().ContainSingle()
            .Which.Id.Should().Be((int)AuditAction.ProviderUpstreamKeyRotated);
    }

    [TestMethod]
    public async Task Update_UnchangedUpstreamKey_AuditsConfigUpdateOnly()
    {
        IServiceProvider services = GetServices();
        var audit = new RecordingAuditLogger();
        var controller = ResolveController(services, audit: audit);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        await controller.Update(
            provider.Id,
            new UpdateModelProviderRequest("Renamed", provider.Endpoint.ToString(), provider.ApiKey, provider.Kind),
            CancellationToken);

        audit.Events.Should().ContainSingle()
            .Which.Id.Should().Be((int)AuditAction.ProviderConfigUpdated);
    }

    [TestMethod]
    public async Task Update_ChangedKeyAndName_AuditsRotationAndConfigUpdate()
    {
        IServiceProvider services = GetServices();
        var audit = new RecordingAuditLogger();
        var controller = ResolveController(services, audit: audit);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        await controller.Update(
            provider.Id,
            new UpdateModelProviderRequest("Renamed", provider.Endpoint.ToString(), "sk-rotated", provider.Kind),
            CancellationToken);

        audit.Events.Select(e => e.Id).Should().BeEquivalentTo(
            [(int)AuditAction.ProviderUpstreamKeyRotated, (int)AuditAction.ProviderConfigUpdated]);
    }

    [TestMethod]
    public async Task Update_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Update(
            Guid.NewGuid(),
            new UpdateModelProviderRequest("X", "https://x.test/", "k", ModelProviderKind.OpenAi),
            CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        var result = await controller.Delete(provider.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task CreateModel_DuplicateModelName_ReturnsConflict()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        var result = await controller.CreateModel(
            endpoint.Provider.Id,
            new CreateModelEndpointRequest(endpoint.Model.Name, 0.001m, 0.002m),
            CancellationToken);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [TestMethod]
    public async Task CreateModel_UnknownProvider_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.CreateModel(
            Guid.NewGuid(),
            new CreateModelEndpointRequest("gpt-4o-mini", 0.001m, 0.002m),
            CancellationToken);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [TestMethod]
    public async Task UpdateModelPricing_PersistsNewCosts()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        var result = await controller.UpdateModelPricing(
            endpoint.Provider.Id,
            endpoint.Id,
            new UpdateModelEndpointPricingRequest(9.99m, 19.99m),
            CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.InputTokenCost.Should().Be(9.99m);
        result.Value.OutputTokenCost.Should().Be(19.99m);
    }

    [TestMethod]
    public async Task DeleteModel_Existing_ReturnsNoContent()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        var result = await controller.DeleteModel(endpoint.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task CreateKey_UnknownProject_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        var result = await controller.CreateKey(
            provider.Id,
            new CreateApiKeyRequest("k", Guid.NewGuid()),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task CreateKey_PersistsKey()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var owner = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var result = await controller.CreateKey(
            provider.Id,
            new CreateApiKeyRequest("dev-key", project.Id, UserId: owner.Id),
            CancellationToken);

        var created = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException());
        var dto = (ApiKeyDto?)created.Value;
        dto.Should().NotBeNull();
        dto.Name.Should().Be("dev-key");
        dto.ProjectId.Should().Be(project.Id);
        dto.OwnerId.Should().Be(owner.Id);
    }

    [TestMethod]
    public async Task CreateKey_ReturnsPlaintextOnce_ThenListShowsPrefixOnly()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var owner = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var result = await controller.CreateKey(
            provider.Id,
            new CreateApiKeyRequest("dev-key", project.Id, UserId: owner.Id),
            CancellationToken);

        // The create response carries the plaintext exactly once...
        var created = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException());
        var dto = (ApiKeyDto?)created.Value;
        dto.Should().NotBeNull();
        dto.PlaintextKey.Should().NotBeNullOrEmpty();
        dto.PlaintextKey.Should().StartWith("proxytrace-");
        dto.KeyPrefix.Should().NotBeNullOrEmpty();

        // ...and the list never exposes it again, only the masked prefix.
        var listed = await controller.GetKeys(provider.Id, CancellationToken);
        listed.Should().ContainSingle();
        listed[0].PlaintextKey.Should().BeNull();
        listed[0].KeyPrefix.Should().StartWith("proxytrace-");
    }

    [TestMethod]
    public async Task DeleteKey_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        var result = await controller.DeleteKey(provider.Id, Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Create_DiscoveredModel_CreatesEndpointWithPrices()
    {
        var providerClient = Substitute.For<IProviderClient>();
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance<IProviderClient.Factory>(_ => providerClient));
        var model = await services.GetRequiredService<IModelRepository>().GetOrCreateAsync("gpt-4o", CancellationToken);
        providerClient.GetModelsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PricedModel>>([new PricedModel(model, new ModelPrice(2.5m, 10.0m))]));

        var controller = ResolveController(services);

        await controller.Create(
            new CreateModelProviderRequest("Acme", "https://api.acme.test/", "sk-test", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        var endpointRepo = services.GetRequiredService<IModelEndpointRepository>();
        var all = await endpointRepo.GetAllAsync(CancellationToken);
        var endpoint = all.Should().ContainSingle().Subject;
        endpoint.Model.Name.Should().Be("gpt-4o");
        endpoint.InputTokenCost.Should().Be(2.5m);
        endpoint.OutputTokenCost.Should().Be(10.0m);
    }

    [TestMethod]
    public async Task Reload_NoDuplicates_WhenModelAlreadyExists()
    {
        var providerClient = Substitute.For<IProviderClient>();
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance<IProviderClient.Factory>(_ => providerClient));
        var model = await services.GetRequiredService<IModelRepository>().GetOrCreateAsync("gpt-4o", CancellationToken);
        providerClient.GetModelsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PricedModel>>([new PricedModel(model, new ModelPrice(2.5m, 10.0m))]));

        var controller = ResolveController(services);

        // First: create the provider (which auto-populates models)
        var createResult = await controller.Create(
            new CreateModelProviderRequest("Acme", "https://api.acme.test/", "sk-test", ModelProviderKind.OpenAiCompatible),
            CancellationToken);
        var createdDto = (ModelProviderDto?)((CreatedAtActionResult)(createResult.Result ?? throw new InvalidOperationException())).Value;
        createdDto.Should().NotBeNull();
        var providerId = createdDto.Id;

        // Second: reload — same model discovered again
        var reloadResult = await controller.Reload(providerId, CancellationToken);

        var endpoints = (IReadOnlyList<ModelEndpointDto>?)reloadResult.Value;
        endpoints.Should().ContainSingle(e => e.ModelName == "gpt-4o");

        var endpointRepo = services.GetRequiredService<IModelEndpointRepository>();
        var allEndpoints = await endpointRepo.GetAllAsync(CancellationToken);
        allEndpoints.Where(e => e.Provider.Id == providerId).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Reload_CreatesEndpointsFromClientModelsWithResolvedPrices()
    {
        var providerClient = Substitute.For<IProviderClient>();
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance<IProviderClient.Factory>(_ => providerClient));
        var model = await services.GetRequiredService<IModelRepository>().GetOrCreateAsync("gpt-4o", CancellationToken);
        providerClient.GetModelsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PricedModel>>([new PricedModel(model, new ModelPrice(1.0m, 2.0m))]));

        var controller = ResolveController(services);

        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);

        await controller.Reload(provider.Id, CancellationToken);

        var endpointRepo = services.GetRequiredService<IModelEndpointRepository>();
        var endpoints = await endpointRepo.GetByProviderAsync(provider.Id, CancellationToken);
        var endpoint = endpoints.Should().ContainSingle().Subject;
        endpoint.Model.Name.Should().Be("gpt-4o");
        endpoint.InputTokenCost.Should().Be(1.0m);
        endpoint.OutputTokenCost.Should().Be(2.0m);
    }

    [TestMethod]
    public async Task Reload_UpdatesExistingModelPrice()
    {
        var providerClient = Substitute.For<IProviderClient>();
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance<IProviderClient.Factory>(_ => providerClient));
        var model = await services.GetRequiredService<IModelRepository>().GetOrCreateAsync("gpt-4o", CancellationToken);

        // Create the provider with an initial price.
        providerClient.GetModelsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PricedModel>>([new PricedModel(model, new ModelPrice(1.0m, 2.0m))]));
        var controller = ResolveController(services);
        var createResult = await controller.Create(
            new CreateModelProviderRequest("Acme", "https://api.acme.test/", "sk-test", ModelProviderKind.OpenAiCompatible),
            CancellationToken);
        var createdDto = (ModelProviderDto?)((CreatedAtActionResult)(createResult.Result ?? throw new InvalidOperationException())).Value;
        createdDto.Should().NotBeNull();
        var providerId = createdDto.Id;

        // Reload with a different price for the same model → the existing endpoint is updated.
        providerClient.GetModelsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PricedModel>>([new PricedModel(model, new ModelPrice(3.0m, 9.0m))]));
        await controller.Reload(providerId, CancellationToken);

        var endpoints = await services.GetRequiredService<IModelEndpointRepository>().GetByProviderAsync(providerId, CancellationToken);
        var endpoint = endpoints.Should().ContainSingle().Subject;
        endpoint.InputTokenCost.Should().Be(3.0m);
        endpoint.OutputTokenCost.Should().Be(9.0m);
    }

    [TestMethod]
    public async Task Reload_UnknownProvider_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Reload(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [TestMethod]
    public async Task GetAvailableModels_WhenProviderRejectsKey_ReturnsBadRequest()
    {
        var providerClient = Substitute.For<IProviderClient>();
        providerClient.GetModelsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PricedModel>>(_ => throw new ProviderConnectionException(
                ProviderConnectionError.Unauthorized,
                new HttpRequestException("Unauthorized")));
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance<IProviderClient.Factory>(_ => providerClient));
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var controller = ResolveController(services);

        var result = await controller.GetAvailableModels(provider.Id, CancellationToken);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be(nameof(ProviderConnectionError.Unauthorized));
    }

    // Captures the EventIds the controller audits, so tests can assert which AuditAction was
    // recorded (same shape as AuditDeniedAccessMiddlewareTests.RecordingAuditLogger).
    private sealed class RecordingAuditLogger : Microsoft.Extensions.Logging.ILogger<Audit>
    {
        public List<Microsoft.Extensions.Logging.EventId> Events { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Events.Add(eventId);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private static ModelProvidersController ResolveController(
        IServiceProvider services,
        Microsoft.Extensions.Logging.ILogger<Audit>? audit = null) => new(
        services.GetRequiredService<IModelProviderRepository>(),
        services.GetRequiredService<IApiKeyRepository>(),
        services.GetRequiredService<IProjectRepository>(),
        services.GetRequiredService<IModelEndpointRepository>(),
        services.GetRequiredService<IModelProvider.CreateNew>(),
        services.GetRequiredService<IModelProvider.CreateExisting>(),
        services.GetRequiredService<IApiKey.CreateNew>(),
        services.GetRequiredService<IModelRepository>(),
        services.GetRequiredService<IModelEndpoint.CreateNew>(),
        services.GetRequiredService<IModelEndpoint.CreateExisting>(),
        services.GetRequiredService<ModelProviderDtoMapper>(),
        services.GetRequiredService<IModelPriceRefresher>(),
        services.GetRequiredService<ICurrentUserAccessor>(),
        services.GetRequiredService<IRepository<IUser>>(),
        audit ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance);
}
