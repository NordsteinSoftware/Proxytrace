using Nordstein.Core.AI.Clients;
using NSubstitute;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.AI.Serialization;

namespace Proxytrace.Application.Tests;

internal sealed class CannedJsonAgent : IAgent
{
    private readonly string cannedResponse;
    private readonly IOutputFormat.Create outputFormatFactory;

    public CannedJsonAgent(string cannedResponse, IOutputFormat.Create outputFormatFactory)
    {
        this.cannedResponse = cannedResponse;
        this.outputFormatFactory = outputFormatFactory;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; } = DateTimeOffset.UtcNow;
    public string Name => "canned";
    public IModelEndpoint Endpoint { get; } = Substitute.For<IModelEndpoint>();
    public IProject Project { get; } = Substitute.For<IProject>();
    public IAgentVersion CurrentVersion => throw new NotImplementedException();
    public IPromptTemplate SystemPrompt { get; } = Substitute.For<IPromptTemplate>();
    public IReadOnlyList<ToolSpecification> Tools => [];
    public IModelParameters ModelParameters { get; } = Substitute.For<IModelParameters>();
    public bool IsSystemAgent => true;

    public IModelClient CreateClient(IModelEndpoint? customEndpoint = null, bool skipIngestion = false)
        => new CannedJsonClient(cannedResponse, outputFormatFactory);

    public Task<IAgent> ChangeEndpoint(IModelEndpoint modelEndpoint, CancellationToken cancellationToken = default)
        => Task.FromResult<IAgent>(this);

    public Task<IAgent> ChangeModelParameters(IModelParameters modelParameters, CancellationToken cancellationToken = default)
        => Task.FromResult<IAgent>(this);

    public Task<IAgent> ChangeSystemMessage(IPromptTemplate systemPrompt, CancellationToken cancellationToken = default)
        => Task.FromResult<IAgent>(this);

    public Task<IAgent> ChangeTools(IReadOnlyList<ToolSpecification> tools, CancellationToken cancellationToken = default)
        => Task.FromResult<IAgent>(this);

    public Task<IAgent> CreateNewVersionAsync(IPromptTemplate systemPrompt, IReadOnlyList<ToolSpecification> tools, CancellationToken cancellationToken = default)
        => Task.FromResult<IAgent>(this);

    public SystemMessage CreateSystemMessage(IReadOnlyDictionary<string, string>? variables = null)
        => new([Content.FromText("canned")]);

    public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(
        System.ComponentModel.DataAnnotations.ValidationContext validationContext) => [];

    private sealed class CannedJsonClient : IModelClient
    {
        private readonly string cannedResponse;
        private readonly IOutputFormat.Create outputFormatFactory;

        public CannedJsonClient(string cannedResponse, IOutputFormat.Create outputFormatFactory)
        {
            this.cannedResponse = cannedResponse;
            this.outputFormatFactory = outputFormatFactory;
        }

        public Task<ICompletion> CompleteAsync(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public ModelRequestPreview BuildRequestPreview(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null)
            => throw new NotImplementedException();

        public async Task<TypedCompletion<TOutput>> CompleteAsync<TOutput>(
            Conversation conversation,
            ModelOptions? options = null,
            IReadOnlyDictionary<string, string>? promptVariables = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var output = await outputFormatFactory(typeof(TOutput))
                    .ParseAsync<TOutput>(cannedResponse, cancellationToken);
                return new TypedCompletion<TOutput>(output, null, TimeSpan.FromMilliseconds(100));
            }
            catch
            {
                return new TypedCompletion<TOutput>(default, null, TimeSpan.Zero);
            }
        }

        public async IAsyncEnumerable<ModelStreamUpdate> StreamAsync(
            SystemMessage systemMessage,
            Conversation conversation,
            ModelOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public void Dispose()
        {
        }
    }

    public Task<IAgent> ReloadAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IAgent> AddAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IAgent> UpdateAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IAgent> UpsertAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
