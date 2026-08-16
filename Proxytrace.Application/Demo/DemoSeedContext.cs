using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.User;

// ReSharper disable InconsistentNaming

namespace Proxytrace.Application.Demo;

internal sealed class DemoSeedContext
{
    /// <summary>
    /// Gets or sets the demo user.
    /// </summary>
    public IUser? DemoUser { get; set; }
    /// <summary>
    /// Gets or sets the project.
    /// </summary>
    public IProject? Project { get; set; }

    /// <summary>
    /// The real model provider created from <c>Kiosk:Endpoint</c> (populated only when a live endpoint
    /// is configured). The seeded demo ingestion API key points at it so the in-process proxy forwards
    /// to the live upstream. Null in a demo without a live endpoint.
    /// </summary>
    public IModelProvider? KioskLiveProvider { get; set; }

    /// <summary>
    /// Gets or sets the gpt54 endpoint.
    /// </summary>
    public IModelEndpoint? Gpt54Endpoint { get; set; }
    /// <summary>
    /// Gets or sets the gpt54 mini endpoint.
    /// </summary>
    public IModelEndpoint? Gpt54MiniEndpoint { get; set; }
    /// <summary>
    /// Gets or sets the claude endpoint.
    /// </summary>
    public IModelEndpoint? ClaudeEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the customer support agent.
    /// </summary>
    public IAgent? CustomerSupportAgent { get; set; }
    /// <summary>
    /// Gets or sets the code review agent.
    /// </summary>
    public IAgent? CodeReviewAgent { get; set; }
    /// <summary>
    /// Gets or sets the data analytics agent.
    /// </summary>
    public IAgent? DataAnalyticsAgent { get; set; }
    /// <summary>
    /// Gets or sets the email triage agent.
    /// </summary>
    public IAgent? EmailTriageAgent { get; set; }

    /// <summary>
    /// Gets or sets the helpfulness.
    /// </summary>
    public IAgenticEvaluator? Helpfulness { get; set; }
    /// <summary>
    /// Gets or sets the politeness.
    /// </summary>
    public IAgenticEvaluator? Politeness { get; set; }

    /// <summary>
    /// Gets the suites by key.
    /// </summary>
    public Dictionary<string, ITestSuite> SuitesByKey { get; } = new();
    /// <summary>
    /// Gets the all runs.
    /// </summary>
    public List<ITestRun> AllRuns { get; } = [];

    /// <summary>
    /// System A/B candidate runs seeded per agent id, so validated/invalidated theories and
    /// proposals can reference a real (hidden) A/B test run instead of a user-facing one.
    /// </summary>
    public Dictionary<Guid, ITestRun> AbCandidateRunsByAgent { get; } = new();

    /// <summary>
    /// The freshly regressed triage run group and the endpoint-down tone group — shaped so the
    /// real anomaly detector fires on them during seeding.
    /// </summary>
    public ITestRunGroup? RegressedTriageGroup { get; set; }
    /// <summary>
    /// Gets or sets the failed tone group.
    /// </summary>
    public ITestRunGroup? FailedToneGroup { get; set; }

    /// <summary>
    /// Require demo user.
    /// </summary>
    public IUser RequireDemoUser() => DemoUser ?? throw Missing(nameof(DemoUser));
    /// <summary>
    /// Require project.
    /// </summary>
    public IProject RequireProject() => Project ?? throw Missing(nameof(Project));
    /// <summary>
    /// Require kiosk live provider.
    /// </summary>
    public IModelProvider RequireKioskLiveProvider() => KioskLiveProvider ?? throw Missing(nameof(KioskLiveProvider));
    /// <summary>
    /// Require gpt54 endpoint.
    /// </summary>
    public IModelEndpoint RequireGpt54Endpoint() => Gpt54Endpoint ?? throw Missing(nameof(Gpt54Endpoint));
    /// <summary>
    /// Require gpt54 mini endpoint.
    /// </summary>
    public IModelEndpoint RequireGpt54MiniEndpoint() => Gpt54MiniEndpoint ?? throw Missing(nameof(Gpt54MiniEndpoint));
    /// <summary>
    /// Require claude endpoint.
    /// </summary>
    public IModelEndpoint RequireClaudeEndpoint() => ClaudeEndpoint ?? throw Missing(nameof(ClaudeEndpoint));
    /// <summary>
    /// Require customer support agent.
    /// </summary>
    public IAgent RequireCustomerSupportAgent() => CustomerSupportAgent ?? throw Missing(nameof(CustomerSupportAgent));
    /// <summary>
    /// Require code review agent.
    /// </summary>
    public IAgent RequireCodeReviewAgent() => CodeReviewAgent ?? throw Missing(nameof(CodeReviewAgent));
    /// <summary>
    /// Require data analytics agent.
    /// </summary>
    public IAgent RequireDataAnalyticsAgent() => DataAnalyticsAgent ?? throw Missing(nameof(DataAnalyticsAgent));
    /// <summary>
    /// Require email triage agent.
    /// </summary>
    public IAgent RequireEmailTriageAgent() => EmailTriageAgent ?? throw Missing(nameof(EmailTriageAgent));
    /// <summary>
    /// Require regressed triage group.
    /// </summary>
    public ITestRunGroup RequireRegressedTriageGroup() => RegressedTriageGroup ?? throw Missing(nameof(RegressedTriageGroup));
    /// <summary>
    /// Require failed tone group.
    /// </summary>
    public ITestRunGroup RequireFailedToneGroup() => FailedToneGroup ?? throw Missing(nameof(FailedToneGroup));
    /// <summary>
    /// Require helpfulness.
    /// </summary>
    public IAgenticEvaluator RequireHelpfulness() => Helpfulness ?? throw Missing(nameof(Helpfulness));
    /// <summary>
    /// Require politeness.
    /// </summary>
    public IAgenticEvaluator RequirePoliteness() => Politeness ?? throw Missing(nameof(Politeness));

    private static InvalidOperationException Missing(string name)
        => new($"DemoSeedContext.{name} was not populated. Make sure the earlier scenarios ran first.");
}
