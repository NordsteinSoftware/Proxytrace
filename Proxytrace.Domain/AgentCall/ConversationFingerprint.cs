using System.Security.Cryptography;
using System.Text.Json;
using Nordstein.Core.AI.Messages;

namespace Proxytrace.Domain.AgentCall;

/// <summary>Stable fingerprints used to link a request to the call whose response it continues.</summary>
public static class ConversationFingerprint
{
    public static string? Completed(Conversation request, AssistantMessage? response)
        => response is null ? null : Hash([.. request.Messages, response]);

    public static string? Parent(Conversation request)
    {
        var lastAssistant = -1;
        for (var i = request.Messages.Count - 1; i >= 0; i--)
        {
            if (request.Messages[i] is AssistantMessage)
            {
                lastAssistant = i;
                break;
            }
        }

        return lastAssistant < 0 || lastAssistant == request.Messages.Count - 1
            ? null
            : Hash(request.Messages.Take(lastAssistant + 1));
    }

    private static string Hash(IEnumerable<Message> messages)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(messages.ToArray())));
}
