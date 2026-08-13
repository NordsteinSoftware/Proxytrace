using System.Text.Json;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Tools.Internal;

internal class ToolArgumentGenerator : DomainObjectGenerator<IToolArgument>
{
    public ToolArgumentGenerator(IRandom random) : base(random)
    {
    }

    public override Task<IToolArgument> CreateAsync(CancellationToken cancellationToken = default)
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "string",
            description = random.String()
        });
        return ((IToolArgument)new JsonToolArgument(
                name: random.String(),
                isRequired: random.Bool(),
                json: schema))
            .ToTaskResult();
    }
}
