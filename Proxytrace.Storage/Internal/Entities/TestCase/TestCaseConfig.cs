using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Storage.Internal.Entities.TestCase;

internal class TestCaseConfig : AbstractEntityConfiguration<TestCaseEntity>, IMapper<ITestCase, TestCaseEntity>
{
    private readonly ITestCase.CreateExisting factory;
    private readonly ISerializer serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestCaseConfig"/> class.
    /// </summary>
    public TestCaseConfig(ITestCase.CreateExisting factory, ISerializer serializer)
    {
        this.factory = factory;
        this.serializer = serializer;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<TestCaseEntity> builder)
    {
        builder
            .Property(e => e.Input)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.DeserializeRequired<Conversation>(v)
            );

        builder
            .Property(e => e.ExpectedOutput)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.DeserializeRequired<AssistantMessage>(v)
            );
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<ITestCase> Map(TestCaseEntity stored, CancellationToken cancellationToken = default)
        => factory(stored.Input, stored.ExpectedOutput, stored.SourceAgentCallId, stored).ToTaskResult();

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<TestCaseEntity> Map(ITestCase domain, CancellationToken cancellationToken = default)
        => new TestCaseEntity
        {
            Id = domain.Id,
            Input = domain.Input,
            ExpectedOutput = domain.ExpectedOutput,
            SourceAgentCallId = domain.SourceAgentCallId,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
