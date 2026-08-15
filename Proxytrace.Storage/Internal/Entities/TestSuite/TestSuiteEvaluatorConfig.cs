using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Storage.Internal.Entities.Evaluator;

namespace Proxytrace.Storage.Internal.Entities.TestSuite;

internal class TestSuiteEvaluatorConfig : AbstractEntityConfiguration<TestSuiteEvaluatorEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<TestSuiteEvaluatorEntity> builder)
    {
        builder.HasKey(e => new { e.TestSuiteId, e.EvaluatorId });

        builder
            .HasOne<TestSuiteEntity>()
            .WithMany(e => e.TestSuiteEvaluators)
            .HasForeignKey(e => e.TestSuiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<EvaluatorEntity>()
            .WithMany()
            .HasForeignKey(e => e.EvaluatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.EvaluatorId);
    }
}
