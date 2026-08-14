using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Storage.Internal.Entities.TestCase;

[StoredDomainEntity(typeof(ITestCase))]
internal record TestCaseEntity : Entity
{
    /// <summary>
    /// <see cref="Proxytrace.Domain.TestCase.ITestCase.Input"/> - stored as JSON in the database
    /// </summary>
    public required Conversation Input { get; init; }

    /// <summary>
    /// <see cref="Proxytrace.Domain.TestCase.ITestCase.ExpectedOutput"/> - stored as JSON in the database
    /// </summary>
    public required AssistantMessage ExpectedOutput { get; init; }

    /// <summary>
    /// <see cref="Proxytrace.Domain.TestCase.ITestCase.SourceAgentCallId"/> — a denormalized snapshot of
    /// the source trace id (no FK): the case must survive deletion of the high-volume trace it came from,
    /// so this deliberately holds a plain nullable <see cref="System.Guid"/> rather than a foreign key.
    /// </summary>
    public Guid? SourceAgentCallId { get; init; }
}
