using System.Text.RegularExpressions;
using Nordstein.Core.AI.Messages;

namespace Proxytrace.Storage.Internal.Entities.AgentCall;

/// <summary>
/// Builds the denormalised <see cref="AgentCallEntity.RequestPreview"/> shown in the traces list:
/// the first user message in the request, whitespace-collapsed and truncated. Computed once at write
/// time (ingestion) and on the one-time <see cref="AgentCallPreviewBackfillService"/> backfill, so the
/// list query can project a scalar column without reading/deserialising the full request payload.
/// </summary>
internal static partial class AgentCallPreview
{
    public const int MaxLength = 1000;

    /// <summary>First user message in the request, whitespace-collapsed and truncated to
    /// <see cref="MaxLength"/>; <c>null</c> when the request has no user message.</summary>
    public static string? Build(Conversation request)
    {
        string? text = request.Messages.OfType<UserMessage>().FirstOrDefault()?.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string collapsed = Whitespace().Replace(text, " ").Trim();
        return collapsed.Length > MaxLength ? collapsed[..MaxLength] : collapsed;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
