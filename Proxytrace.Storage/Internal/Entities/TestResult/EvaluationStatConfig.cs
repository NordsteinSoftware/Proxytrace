using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Proxytrace.Storage.Internal.Entities.TestResult;

internal class EvaluationStatConfig : AbstractEntityConfiguration<EvaluationStatEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<EvaluationStatEntity> builder)
    {
        builder.HasKey(e => e.Id);

        // Cascade so a deleted/aged-out test result takes its projection rows with it. (Like the
        // other FK semantics, cascade is enforced only on PostgreSQL — see docs/database.md.)
        builder
            .HasOne<TestResultEntity>()
            .WithMany(e => e.EvaluationStats)
            .HasForeignKey(e => e.TestResultId)
            .OnDelete(DeleteBehavior.Cascade);

        // The evaluator-stats queries filter by EvaluatorId, then range/bucket by CreatedAt; this
        // composite index serves both from one index (its leading column also covers the
        // evaluator-scoped filter). Mirrors the AgentCall (AgentVersionId, CreatedAt) tuning.
        builder.HasIndex(e => new { e.EvaluatorId, e.CreatedAt });
    }
}
