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
    // TestDataResetCoverageTests fails when a table of the model is neither cleared (listed here,
    // or reached by CASCADE through a foreign key into one of these) nor listed in BaselineTables.
    internal static readonly IReadOnlyList<string> ContentTables =
    [
        "AgentCallEntity", "AgentVersionEntity", "AgentEntity",
        "EvaluatorEntity", "TestSuiteEvaluatorEntity", "TestCaseEntity", "TestSuiteEntity",
        "TestResultEntity", "TestRunEntity", "TestRunStatsEntity", "TestRunGroupEntity",
        "OptimizationProposalEntity", "InviteEntity", "PasswordResetTokenEntity",
        "UserTotpEnrollmentEntity", "MfaBackupCodeEntity",
        "ApplicationErrorEntity", "AuditLogEntryEntity", "NotificationEntity",
        // Project-owned groupings keyed by a deterministic (project, key) id: left behind, the next
        // test that uses the same key would get the previous test's row back, counters and all.
        "SessionEntity", "ScopeEntity",
    ];

    /// <summary>The setup baseline every reset keeps.</summary>
    internal static readonly IReadOnlyList<string> BaselineTables =
    [
        "UserEntity", "ProjectEntity", "ProjectUserEntity", "ProjectSearchSettingsEntity",
        "ModelProviderEntity", "ModelEntity", "ModelEndpointEntity", "ApiKeyEntity",
        "EmailSettingsEntity", "OutlierSettingsEntity", "StoredLicenseEntity",
    ];

    private static readonly string TruncateSql =
        $"TRUNCATE TABLE {string.Join(", ", ContentTables.Select(t => $"\"{t}\""))} CASCADE;";

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
#pragma warning disable EF1002 // constant SQL built only from the static table list above — no input
        => await contextFactory().Database.ExecuteSqlRawAsync(TruncateSql, cancellationToken);
#pragma warning restore EF1002
}
