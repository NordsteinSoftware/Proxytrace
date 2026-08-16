using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Proxytrace.Api;

/// <summary>
/// Marks an endpoint as test-support only. The endpoint responds normally only when the host is in
/// the Development environment or <c>TestSupport:Enabled</c> is configured true (the e2e stack sets
/// it); otherwise it returns 404, so destructive/seed helpers can never be reached on a real
/// deployment. Applied to the test reset/log endpoints and the per-controller <c>/seed</c> helpers.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TestOnlyEndpointAttribute : Attribute, IFilterFactory
{
    /// <summary>
    /// Gets the is reusable.
    /// </summary>
    public bool IsReusable => true;

    /// <summary>
    /// Creates the instance.
    /// </summary>
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new Filter(
            serviceProvider.GetRequiredService<IHostEnvironment>(),
            serviceProvider.GetRequiredService<IConfiguration>());

    private sealed class Filter : IActionFilter
    {
        private readonly IHostEnvironment environment;
        private readonly IConfiguration configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="Filter"/> class.
        /// </summary>
        public Filter(IHostEnvironment environment, IConfiguration configuration)
        {
            this.environment = environment;
            this.configuration = configuration;
        }

        /// <summary>
        /// On action executing.
        /// </summary>
        public void OnActionExecuting(ActionExecutingContext context)
        {
            bool enabled = environment.IsDevelopment()
                || configuration.GetValue<bool>("TestSupport:Enabled");
            if (!enabled)
            {
                context.Result = new NotFoundResult();
            }
        }

        /// <summary>
        /// On action executed.
        /// </summary>
        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
