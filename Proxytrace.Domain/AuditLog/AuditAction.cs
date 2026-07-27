namespace Proxytrace.Domain.AuditLog;

/// <summary>
/// The kind of audited system action. Values are stable (persisted as ints and reused as the
/// <c>EventId</c> of the audit log messages) — append new members, never renumber existing ones.
/// </summary>
public enum AuditAction
{
    TestRunStarted = 0,
    ApiKeyMinted = 1,
    ApiKeyDeleted = 2,
    ProjectDeleted = 3,
    ProjectMemberAdded = 4,
    ProjectMemberRemoved = 5,
    LicenseSet = 6,
    LicenseRemoved = 7,
    TestSuiteDeleted = 8,
    EvaluatorDeleted = 9,
    TestCaseDeleted = 10,
    ProviderConfigCreated = 11,
    ProviderConfigUpdated = 12,
    EndpointConfigCreated = 13,
    EndpointConfigUpdated = 14,
    ProviderConfigDeleted = 15,
    EndpointConfigDeleted = 16,
    UserRoleChanged = 17,
    UserDeleted = 18,
    AgentDeleted = 19,

    // Creation / modification of project-scoped entities (the delete counterparts are above).
    ProjectCreated = 20,
    ProjectRenamed = 21,
    AgentEndpointChanged = 22,
    EvaluatorCreated = 23,
    EvaluatorUpdated = 24,
    TestSuiteCreated = 25,
    TestSuiteUpdated = 26,
    TestCaseCreated = 27,

    // Authentication & identity lifecycle.
    UserInvited = 28,
    InviteRevoked = 29,
    UserSignedUp = 30,
    UserLoggedIn = 31,
    LoginFailed = 32,
    UserLoggedOut = 33,
    AdminBootstrapped = 34,
    LegacyAccountClaimed = 35,

    // Optimization proposal state transitions (e.g. via the MCP server).
    ProposalStatusChanged = 36,

    // Operator email/SMTP configuration.
    EmailSettingsUpdated = 37,

    // Password reset (forgot-password flow + admin-issued reset link).
    PasswordResetRequested = 38,
    PasswordResetCompleted = 39,
    PasswordResetLinkIssued = 40,

    // Multi-factor authentication (TOTP). UserLoggedIn still marks a successful MFA login; these add
    // the enable/disable lifecycle and the failed second-factor attempt (the analog of LoginFailed).
    MfaEnabled = 41,
    MfaDisabled = 42,
    MfaChallengeFailed = 43,

    // Optimization theory lifecycle. Submit/reset/reject are user-initiated; validated/invalidated
    // and proposal generation are decided by the background A/B validation pipeline (System actor).
    TheorySubmitted = 44,
    TheoryReset = 45,
    TheoryRejected = 46,
    TheoryValidated = 47,
    TheoryInvalidated = 48,
    ProposalGenerated = 49,
    ProposalAutoAdopted = 50,

    // Test-run lifecycle (groups, individual runs) and recurring schedules.
    TestRunGroupOptimizeRequested = 51,
    TestRunGroupCancelled = 52,
    TestRunGroupDeleted = 53,
    TestRunDeleted = 54,
    TestRunScheduleCreated = 55,
    TestRunScheduleUpdated = 56,
    TestRunScheduleDeleted = 57,
    TestRunScheduleRunNow = 58,

    // Trace deletion and agent-version moves.
    AgentCallDeleted = 59,
    AgentVersionMoved = 60,

    // Destructive operator action: purge of all non-model domain data.
    SetupCleanupPurged = 61,

    // One-time at-rest protection of plaintext secrets (System actor).
    SecretsBackfilled = 62,

    // Authorization denial on a state-changing request (recorded with AuditOutcome.Failure).
    AccessDenied = 63,

    // Operator outlier-detection sensitivity configuration.
    OutlierSettingsUpdated = 64,

    // Editing a test case's expected output (the project is resolved via the suite->agent reverse
    // projection ITestSuiteRepository.GetProjectIdByTestCaseAsync, since a test case has no FK to a
    // project of its own).
    TestCaseUpdated = 65,

    // User-defined LLM-based anomaly detector lifecycle.
    CustomAnomalyDetectorCreated = 66,
    CustomAnomalyDetectorUpdated = 67,
    CustomAnomalyDetectorDeleted = 68,

    // A theory's A/B validation errored out (unreachable provider, incomplete run) — the theory
    // settled Failed instead of Invalidated and can be retried (System actor).
    TheoryValidationFailed = 69,

    // A provider's upstream API key was replaced (credential rotation) — recorded distinctly from
    // ProviderConfigUpdated so security review can find rotations without diffing configurations.
    // The key value itself is never recorded.
    ProviderUpstreamKeyRotated = 70,

    // An administrator read a provider's upstream API key back in the clear. The key used to ship
    // with every providers-page load, so nothing recorded who had actually seen it; it is now
    // returned only from a dedicated endpoint, and each read lands here. The key value itself is
    // never recorded.
    ProviderUpstreamKeyRevealed = 71,
}
