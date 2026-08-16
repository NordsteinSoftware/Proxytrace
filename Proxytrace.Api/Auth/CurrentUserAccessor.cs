using Proxytrace.Application.Auth;
using Proxytrace.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Auth;

internal sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    internal const string UserIdItemKey = "Proxytrace.UserId";

    private const string CurrentUserItemKey = "Proxytrace.CurrentUser";

    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IRepository<IUser> users;

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentUserAccessor"/> class.
    /// </summary>
    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor, IRepository<IUser> users)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.users = users;
    }

    /// <summary>
    /// Gets the current user asynchronously.
    /// </summary>
    public async Task<IUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx is null)
        {
            return null;
        }

        if (ctx.Items[CurrentUserItemKey] is IUser cached)
        {
            return cached;
        }

        if (ctx.Items[UserIdItemKey] is not Guid userId)
        {
            return null;
        }

        var user = await users.FindAsync(userId, cancellationToken);
        if (user is not null)
        {
            ctx.Items[CurrentUserItemKey] = user;
        }

        return user;
    }
}
