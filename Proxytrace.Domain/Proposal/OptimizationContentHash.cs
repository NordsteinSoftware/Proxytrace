using System.Security.Cryptography;
using System.Text;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain.OptimizationProposal;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.Proposal;

/// <summary>
/// Single source of truth for the deterministic fingerprint of a proposed agent change.
/// Both <see cref="OptimizationProposal.IOptimizationProposal"/> and
/// <see cref="OptimizationTheory.IOptimizationTheory"/> hash through here, which is what
/// lets a theory deduplicate against an equivalent proposal — the envelopes must stay
/// byte-identical, so they live in exactly one place.
/// </summary>
internal static class OptimizationContentHash
{
    public static string ForSystemPrompt(ISerializer serializer, Guid agentId, string proposedSystemMessage)
        => Hash(serializer, new
        {
            Agent = agentId,
            Kind = ProposalKind.SystemPrompt,
            Payload = new { Message = NormalizeText(proposedSystemMessage) },
        });

    public static string ForModelSwitch(ISerializer serializer, Guid agentId, Guid proposedEndpointId)
        => Hash(serializer, new
        {
            Agent = agentId,
            Kind = ProposalKind.ModelSwitch,
            Payload = new { EndpointId = proposedEndpointId },
        });

    public static string ForTools(ISerializer serializer, Guid agentId, IReadOnlyList<ToolSpecification> proposedTools)
    {
        var orderedTools = proposedTools
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new { t.Name, Description = NormalizeText(t.Description), t.Arguments })
            .ToArray();
        return Hash(serializer, new
        {
            Agent = agentId,
            Kind = ProposalKind.Tool,
            Payload = orderedTools,
        });
    }

    private static string Hash(ISerializer serializer, object envelope)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(serializer.Serialize(envelope)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeText(string value)
        => value.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
}
