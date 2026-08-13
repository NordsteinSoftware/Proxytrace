using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.Usage.Internal;

internal class TokenUsageGenerator : DomainObjectGenerator<TokenUsage>
{
    public TokenUsageGenerator(IRandom random) : base(random)
    {
    }

    public override Task<TokenUsage> CreateAsync(CancellationToken cancellationToken = default)
        => new TokenUsage(
                inputTokenCount: (ulong)random.Int(min: 0, max: 1000),
                outputTokenCount: (ulong)random.Int(min: 0, max: 1000))
            .ToTaskResult();
}
