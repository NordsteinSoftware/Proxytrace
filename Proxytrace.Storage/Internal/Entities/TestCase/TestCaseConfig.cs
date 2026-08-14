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

    public TestCaseConfig(ITestCase.CreateExisting factory, ISerializer serializer)
    {
        this.factory = factory;
        this.serializer = serializer;
    }

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

    public Task<ITestCase> Map(TestCaseEntity stored, CancellationToken cancellationToken = default)
        => factory(stored.Input, stored.ExpectedOutput, stored.SourceAgentCallId, stored).ToTaskResult();

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
