using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Proxytrace.Application.Auth;

namespace Proxytrace.Api.Auth;

internal sealed class RequireLocalModeAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>
    /// On authorization.
    /// </summary>
    public void OnAuthorization(AuthorizationFilterContext ctx)
    {
        var opts = ctx.HttpContext.RequestServices.GetRequiredService<AuthOptions>();
        if (opts.Mode != AuthMode.Local)
        {
            ctx.Result = new NotFoundResult();
        }
    }
}
