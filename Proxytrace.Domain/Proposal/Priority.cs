namespace Proxytrace.Domain.Proposal;

/// <summary>
/// Relative urgency of an optimization theory or proposal, used to surface the most impactful
/// recommendations first in the UI and to set expectations about how quickly a change should be adopted.
/// </summary>
public enum Priority
{
    /// <summary>Minor improvement; adopt when convenient.</summary>
    Low = 0,

    /// <summary>Meaningful improvement; should be reviewed in the near term.</summary>
    Medium = 1,

    /// <summary>Significant gain in quality or efficiency; prioritize for the next review cycle.</summary>
    High = 2,

    /// <summary>Severe regression or large opportunity; requires immediate attention.</summary>
    Critical = 3,
}
