namespace Proxytrace.Storage.Internal.Entities.TestRunSchedule;

/// <summary>
/// Join table for the N:M between schedules and endpoints. Storage-only, no domain counterpart.
/// </summary>
internal record TestRunScheduleEndpointEntity
{
    /// <summary>
    /// Gets or sets the schedule id.
    /// </summary>
    public required Guid ScheduleId { get; init; }
    /// <summary>
    /// Gets or sets the endpoint id.
    /// </summary>
    public required Guid EndpointId { get; init; }
}
