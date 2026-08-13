using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.CostLimit.Internal;

internal record CostLimit : DomainEntity<ICostLimit>, ICostLimit
{
    public IProject Project { get; private init; }
    public IAgent? Agent { get; private init; }
    public IApiKey? ApiKey { get; private init; }
    public decimal? SoftLimitEur { get; private init; }
    public decimal? HardLimitEur { get; private init; }
    public bool Enabled { get; private init; }

    public CostLimit(
        IProject project,
        IAgent? agent,
        IApiKey? apiKey,
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled,
        IRepository<ICostLimit> repository) : base(repository)
    {
        Project = project;
        Agent = agent;
        ApiKey = apiKey;
        SoftLimitEur = softLimitEur;
        HardLimitEur = hardLimitEur;
        Enabled = enabled;
    }

    public CostLimit(
        IProject project,
        IAgent? agent,
        IApiKey? apiKey,
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled,
        IDomainEntityData existing,
        IRepository<ICostLimit> repository) : base(existing, repository)
    {
        Project = project;
        Agent = agent;
        ApiKey = apiKey;
        SoftLimitEur = softLimitEur;
        HardLimitEur = hardLimitEur;
        Enabled = enabled;
    }

    public Task<ICostLimit> Update(
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled,
        CancellationToken cancellationToken = default)
        => ApplyAsync(this with
        {
            SoftLimitEur = softLimitEur,
            HardLimitEur = hardLimitEur,
            Enabled = enabled,
        }, cancellationToken);

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.NotNull(Project);

        if (SoftLimitEur is null && HardLimitEur is null)
            yield return new ValidationResult(
                "A cost limit must set at least a soft or a hard threshold.",
                [nameof(SoftLimitEur), nameof(HardLimitEur)]);

        if (SoftLimitEur is { } soft)
            yield return Validation.Positive(soft, nameof(SoftLimitEur));

        if (HardLimitEur is { } hard)
            yield return Validation.Positive(hard, nameof(HardLimitEur));

        // A soft threshold above the hard one could never fire: the hard limit blocks first.
        if (SoftLimitEur is { } s && HardLimitEur is { } h)
            yield return Validation.LessThanOrEqual(s, h, nameof(SoftLimitEur));

        // An agent-scoped limit must belong to the project it is scoped under, otherwise the
        // guard would attribute another project's spend to it.
        if (Agent is not null && Agent.Project.Id != Project.Id)
            yield return new ValidationResult(
                "An agent-scoped cost limit must reference an agent of the same project.",
                [nameof(Agent)]);

        // Same reasoning for a key: a key belongs to exactly one project, and scoping a limit to a
        // foreign key would measure spend the project never incurred.
        if (ApiKey is not null && ApiKey.Project.Id != Project.Id)
            yield return new ValidationResult(
                "A key-scoped cost limit must reference an API key of the same project.",
                [nameof(ApiKey)]);

        // Exactly one scope. The partial unique indexes assume this — a row with both set would
        // satisfy the agent-scope index while silently escaping the key-scope one.
        if (Agent is not null && ApiKey is not null)
            yield return new ValidationResult(
                "A cost limit is scoped to an agent or to an API key, not to both.",
                [nameof(Agent), nameof(ApiKey)]);
    }
}
