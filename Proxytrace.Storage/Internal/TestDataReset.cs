using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain.TestSupport;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// PostgreSQL implementation of <see cref="ITestDataReset"/>. Truncates the per-run content tables
/// with CASCADE (which clears their dependent rows) while leaving the setup baseline — users,
/// providers, models, endpoints, api keys, projects and their settings/memberships — untouched.
/// </summary>
internal sealed class TestDataReset : ITestDataReset
{
    // Content tables only. None of the preserved baseline tables (Project/Provider/Model/
    // Endpoint/ApiKey/User/…) hold a foreign key INTO these, so CASCADE cannot reach them.
    private const string TruncateSql = """
        TRUNCATE TABLE
          "AgentCallEntity", "AgentVersionEntity", "AgentEntity",
          "EvaluatorEntity", "TestSuiteEvaluatorEntity", "TestCaseEntity", "TestSuiteEntity",
          "TestResultEntity", "TestRunEntity", "TestRunStatsEntity", "TestRunGroupEntity",
          "OptimizationProposalEntity", "InviteEntity", "PasswordResetTokenEntity",
          "UserTotpEnrollmentEntity", "MfaBackupCodeEntity",
          "ApplicationErrorEntity", "AuditLogEntryEntity", "NotificationEntity"
        CASCADE;
        """;

    private readonly Func<StorageDbContext> contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestDataReset"/> class.
    /// </summary>
    public TestDataReset(Func<StorageDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    /// <summary>
    /// Reset asynchronously.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
        => await contextFactory().Database.ExecuteSqlRawAsync(TruncateSql, cancellationToken);
}
