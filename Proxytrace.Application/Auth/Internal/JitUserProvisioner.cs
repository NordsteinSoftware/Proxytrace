using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Internal;

internal class JitUserProvisioner : IJitUserProvisioner
{
    private readonly IUserRepository users;
    private readonly IUser.CreateNew createUser;
    private readonly ITransaction transaction;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="JitUserProvisioner"/> class.
    /// </summary>
    public JitUserProvisioner(
        IUserRepository users,
        IUser.CreateNew createUser,
        ITransaction transaction,
        ILogger<Audit> audit)
    {
        this.users = users;
        this.createUser = createUser;
        this.transaction = transaction;
        this.audit = audit;
    }

    /// <summary>
    /// Ensures the provisioned asynchronously.
    /// </summary>
    public Task<IUser> EnsureProvisionedAsync(
        string externalSubject,
        string email,
        CancellationToken cancellationToken = default)
        => transaction.InvokeAsync(async () =>
        {
            var existing = await users.FindByExternalSubjectAsync(externalSubject, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var total = await users.CountAsync(cancellationToken);
            var role = total == 0 ? UserRole.Admin : UserRole.Member;
            var user = createUser(email, externalSubject, passwordHash: null, role);
            var added = await user.AddAsync(cancellationToken);

            // Close the OIDC auditing gap: just-in-time provisioning is otherwise invisible (sign-in
            // happens at the IdP). The first provisioned user becomes the admin; the rest are members.
            audit.LogAudit(
                role == UserRole.Admin ? AuditAction.AdminBootstrapped : AuditAction.UserSignedUp,
                nameof(IUser), added.Id, added.Email);

            return added;
        });
}
