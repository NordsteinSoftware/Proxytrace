using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Auth.Licensing;

/// <summary>
/// Authorization filter that enforces <see cref="RequiresFeatureAttribute"/> on endpoints.
/// When the required feature is not licensed it short-circuits the request with HTTP 402 and a
/// machine-readable body the frontend uses to surface an upgrade prompt.
/// </summary>
internal sealed class LicenseEnforcementFilter : IAsyncAuthorizationFilter
{
    private readonly ILicenseService licenseService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseEnforcementFilter"/> class.
    /// </summary>
    public LicenseEnforcementFilter(ILicenseService licenseService)
    {
        this.licenseService = licenseService;
    }

    /// <summary>
    /// On authorization asynchronously.
    /// </summary>
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requirement = context.ActionDescriptor.EndpointMetadata
            .OfType<RequiresFeatureAttribute>()
            .LastOrDefault();

        if (requirement is null)
            return Task.CompletedTask;

        if (licenseService.IsFeatureEnabled(requirement.Feature))
            return Task.CompletedTask;

        var tier = licenseService.Current.Tier;
        context.Result = new ObjectResult(new
        {
            error = new
            {
                type = "FeatureNotLicensed",
                feature = requirement.Feature.ToString(),
                tier = tier.ToString(),
                message = $"The feature '{requirement.Feature}' is not available on the '{tier}' tier.",
            },
        })
        {
            StatusCode = StatusCodes.Status402PaymentRequired,
        };

        return Task.CompletedTask;
    }
}
