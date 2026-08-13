using Proxytrace.Domain.AuditLog;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.OutlierSettings;
using Proxytrace.Domain.Outliers;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class OutlierSettingsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Get_WhenUnset_ReturnsActiveDefaults()
    {
        var store = Substitute.For<IOutlierSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns((OutlierSettings?)null);
        var controller = Controller(store);

        var result = await controller.Get(CancellationToken);

        result.Value.Should().NotBeNull().And.Match<OutlierSettingsDto>(d =>
            d.Enabled && d.SigmaMultiplier == 3.0 && d.MinSampleCount == 30 && d.SampleWindow == 200);
    }

    [TestMethod]
    public async Task Update_SavesSettings_AndReturnsDto()
    {
        var store = Substitute.For<IOutlierSettingsStore>();
        var controller = Controller(store);
        var request = new UpdateOutlierSettingsRequest(Enabled: true, SigmaMultiplier: 2.5, MinSampleCount: 40, SampleWindow: 500);

        var result = await controller.Update(request, CancellationToken);

        await store.Received(1).SaveAsync(
            Arg.Is<OutlierSettings>(s => s != null && s.SigmaMultiplier == 2.5 && s.MinSampleCount == 40 && s.SampleWindow == 500),
            Arg.Any<CancellationToken>());
        result.Value.Should().NotBeNull().And.Match<OutlierSettingsDto>(d => d.SampleWindow == 500);
    }

    [TestMethod]
    public async Task Update_WithNonPositiveSigma_ReturnsBadRequest_AndDoesNotSave()
    {
        var store = Substitute.For<IOutlierSettingsStore>();
        var controller = Controller(store);
        var request = new UpdateOutlierSettingsRequest(Enabled: true, SigmaMultiplier: 0, MinSampleCount: 30, SampleWindow: 200);

        var result = await controller.Update(request, CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        await store.DidNotReceive().SaveAsync(Arg.Any<OutlierSettings>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Update_WithZeroSampleWindow_ReturnsBadRequest()
    {
        var store = Substitute.For<IOutlierSettingsStore>();
        var controller = Controller(store);
        var request = new UpdateOutlierSettingsRequest(Enabled: true, SigmaMultiplier: 3, MinSampleCount: 30, SampleWindow: 0);

        var result = await controller.Update(request, CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public void Controller_IsAdminOnly()
    {
        var authorize = typeof(OutlierSettingsController).GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize.Roles.Should().Be(nameof(UserRole.Admin));
    }

    private static OutlierSettingsController Controller(IOutlierSettingsStore store)
        => new(store, new OutlierSettingsDtoMapper(), NullLogger<Audit>.Instance);
}
