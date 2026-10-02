namespace Proxytrace.Domain.AgentCall;

/// <summary>
/// Derived values of an <see cref="IAgentCall"/> that more than one layer must compute identically.
/// </summary>
public static class AgentCallExtensions
{
    /// <summary>
    /// Input plus output tokens of the call's response, or <see langword="null"/> when the provider
    /// reported no usage. The single definition of a call's token total: storage denormalises it onto
    /// the trace row, and the session and scope counters are bumped — and later reversed — by it, so
    /// every one of them must agree or the counters drift from the traces.
    /// </summary>
    public static ulong? TotalTokens(this IAgentCall call)
        => call.Response?.Usage is { } usage ? usage.InputTokenCount + usage.OutputTokenCount : null;

    /// <summary>
    /// <see cref="TotalTokens"/> as the counters store it: a call without reported usage adds nothing.
    /// </summary>
    public static long CountedTokens(this IAgentCall call)
        => (long)(call.TotalTokens() ?? 0);
}
