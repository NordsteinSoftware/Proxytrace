using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

// ReSharper disable UnusedMember.Local

namespace Proxytrace.Storage.Tests;

[TestClass]
public class OptimisticConcurrencyTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Update_WithConflict_Throws()
    {
        IServiceProvider services = GetServices();
        IUser user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>()
            .GetOrCreateAsync(CancellationToken);

        var repo = services.GetRequiredService<IRepository<IUser>>();


        var modifier = new ConcurrentModifier(user);
        var factory = services.GetRequiredService<IUser.CreateExisting>();
        var modified = factory(modifier.Email, modifier.ExternalSubject, modifier.PasswordHash, modifier.Role, modifier.Language, modifier.EmailNotificationsEnabled, modifier.EmailNotificationMinSeverity, modifier);

        await FluentActions.Invoking(() => repo.UpdateAsync(modified, CancellationToken))
            .Should()
            .ThrowAsync<OptimisticConcurrencyException>();
    }

    [TestMethod]
    public async Task Update_SingleActorWithStoredToken_DoesNotConflict()
    {
        // Regression for #202: once UpdatedAt was marked an EF concurrency token, the in-memory
        // provider threw DbUpdateConcurrencyException on ordinary single-actor updates, because the
        // tracked original token was truncated to microseconds (correct only for the relational
        // provider, which persists at that precision) while the in-memory store keeps the full
        // .NET 100ns value. A plain update carrying the entity's own persisted token must succeed.
        IServiceProvider services = GetServices();
        IUser user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>()
            .GetOrCreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IRepository<IUser>>();

        // Reload to carry the exact persisted token, then perform two sequential plain updates.
        IUser reloaded = await repo.GetAsync(user.Id, CancellationToken);

        await FluentActions.Invoking(async () =>
        {
            IUser once = await repo.UpdateAsync(reloaded, CancellationToken);
            await repo.UpdateAsync(once, CancellationToken);
        }).Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Update_TokenDiffersOnlyBelowMicrosecond_DoesNotConflict()
    {
        // Reproduces the Postgres precision bug: the in-memory token keeps .NET's 100ns precision
        // while the database round-trips microseconds, so the first update after an insert sees a
        // sub-microsecond difference that must NOT be treated as a conflict.
        IServiceProvider services = GetServices();
        IUser user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>()
            .GetOrCreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IRepository<IUser>>();

        long ticks = user.UpdatedAt.UtcTicks;
        long bucket = ticks / TimeSpan.TicksPerMicrosecond;
        long differentSubMicrosecond = (ticks % TimeSpan.TicksPerMicrosecond + 1) % TimeSpan.TicksPerMicrosecond;
        // Same microsecond bucket as the stored token, but a different 100ns tick within it.
        var token = new DateTimeOffset(bucket * TimeSpan.TicksPerMicrosecond + differentSubMicrosecond, TimeSpan.Zero);

        var modified = new RetokenedUser(user, token);

        await FluentActions.Invoking(() => repo.UpdateAsync(modified, CancellationToken))
            .Should()
            .NotThrowAsync();
    }

    [TestMethod]
    public async Task Update_TokenDiffersByAMicrosecond_Conflicts()
    {
        // A genuinely older version differs by at least a microsecond and must still be rejected.
        IServiceProvider services = GetServices();
        IUser user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>()
            .GetOrCreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IRepository<IUser>>();

        var token = user.UpdatedAt.AddTicks(-TimeSpan.TicksPerMicrosecond);
        var modified = new RetokenedUser(user, token);

        await FluentActions.Invoking(() => repo.UpdateAsync(modified, CancellationToken))
            .Should()
            .ThrowAsync<OptimisticConcurrencyException>();
    }

    /// <summary>A pass-through view of a user with only its concurrency token (UpdatedAt) overridden.</summary>
    private sealed record RetokenedUser : IUser
    {
        private readonly IUser user;

        public RetokenedUser(IUser user, DateTimeOffset updatedAt)
        {
            this.user = user;
            UpdatedAt = updatedAt;
        }

        public Guid Id => user.Id;
        public DateTimeOffset CreatedAt => user.CreatedAt;
        public DateTimeOffset UpdatedAt { get; }
        public string Email => user.Email;
        public string? ExternalSubject => user.ExternalSubject;
        public string? PasswordHash => user.PasswordHash;
        public UserRole Role => user.Role;
        public string Language => user.Language;
        public bool EmailNotificationsEnabled => user.EmailNotificationsEnabled;
        public NotificationSeverity EmailNotificationMinSeverity => user.EmailNotificationMinSeverity;

        public Task<IUser> ChangeRole(UserRole role, CancellationToken cancellationToken = default) => Task.FromResult<IUser>(this);
        public Task<IUser> ChangePasswordHash(string passwordHash, CancellationToken cancellationToken = default) => Task.FromResult<IUser>(this);
        public Task<IUser> ChangeLanguage(string language, CancellationToken cancellationToken = default) => Task.FromResult<IUser>(this);
        public Task<IUser> ChangeEmailNotificationPreferences(bool emailNotificationsEnabled, NotificationSeverity emailNotificationMinSeverity, CancellationToken cancellationToken = default) => Task.FromResult<IUser>(this);
        public Task<IUser> ReloadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IUser>(this);
        public Task<IUser> AddAsync(CancellationToken cancellationToken = default) => Task.FromResult<IUser>(this);
        public Task<IUser> UpdateAsync(CancellationToken cancellationToken = default) => Task.FromResult<IUser>(this);
        public Task<IUser> UpsertAsync(CancellationToken cancellationToken = default) => Task.FromResult<IUser>(this);
        public Task RemoveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
    }

    private record ConcurrentModifier : IUser
    {
        private readonly IUser user;

        public Guid Id => user.Id;
        public DateTimeOffset CreatedAt => user.CreatedAt.Subtract(TimeSpan.FromHours(1));
        public DateTimeOffset UpdatedAt => user.UpdatedAt.Subtract(TimeSpan.FromHours(1));
        public string Email => "modifier_" + user.Email;
        public string? ExternalSubject => user.ExternalSubject;
        public string? PasswordHash => user.PasswordHash;
        public UserRole Role => user.Role;
        public string Language => user.Language;
        public bool EmailNotificationsEnabled => user.EmailNotificationsEnabled;
        public NotificationSeverity EmailNotificationMinSeverity => user.EmailNotificationMinSeverity;

        public ConcurrentModifier(IUser user)
        {
            this.user = user;
        }

        public Task<IUser> ChangeRole(UserRole role, CancellationToken cancellationToken = default)
            => Task.FromResult<IUser>(this);

        public Task<IUser> ChangePasswordHash(string passwordHash, CancellationToken cancellationToken = default)
            => Task.FromResult<IUser>(this);

        public Task<IUser> ChangeLanguage(string language, CancellationToken cancellationToken = default)
            => Task.FromResult<IUser>(this);

        public Task<IUser> ChangeEmailNotificationPreferences(bool emailNotificationsEnabled, NotificationSeverity emailNotificationMinSeverity, CancellationToken cancellationToken = default)
            => Task.FromResult<IUser>(this);

        public Task<IUser> ReloadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUser>(this);

        public Task<IUser> AddAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUser>(this);

        public Task<IUser> UpdateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUser>(this);

        public Task<IUser> UpsertAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUser>(this);

        public Task RemoveAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            return [];
        }
    }
}
