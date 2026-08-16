using Nordstein.Core.AI.Clients;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.AI.Serialization;

namespace Proxytrace.Infrastructure.Internal;

internal class ModelClient : IModelClient
{
    private readonly IAgent agent;
    private readonly bool skipIngestion;
    private readonly KioskOptions kioskOptions;
    private readonly KioskEndpointOptions kioskEndpoint;
    private readonly ICompletion.Create completionFactory;
    private readonly IAgentCall.CreateNew agentCallFactory;
    private readonly IModelEndpoint endpoint;
    private readonly IOutputFormat.Create outputFormatFactory;
    private readonly IChatClient chatClient;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelClient"/> class.
    /// </summary>
    public ModelClient(
        IAgent agent,
        IModelEndpoint? customEndpoint,
        bool skipIngestion,
        KioskOptions kioskOptions,
        KioskEndpointOptions kioskEndpoint,
        ICompletion.Create completionFactory,
        IAgentCall.CreateNew agentCallFactory,
        IOutputFormat.Create outputFormatFactory)
    {
        endpoint = customEndpoint ?? agent.Endpoint;
        this.agent = agent;
        this.skipIngestion = skipIngestion;
        this.kioskOptions = kioskOptions;
        this.kioskEndpoint = kioskEndpoint;
        this.completionFactory = completionFactory;
        this.agentCallFactory = agentCallFactory;
        this.outputFormatFactory = outputFormatFactory;
        chatClient = CreateChatClient(endpoint);
    }

    internal ModelClient(
        IAgent agent,
        IModelEndpoint? customEndpoint,
        KioskOptions kioskOptions,
        KioskEndpointOptions kioskEndpoint,
        ICompletion.Create completionFactory,
        IAgentCall.CreateNew agentCallFactory,
        IOutputFormat.Create outputFormatFactory,
        IChatClient chatClient)
    {
        endpoint = customEndpoint ?? agent.Endpoint;
        this.agent = agent;
        this.kioskOptions = kioskOptions;
        this.kioskEndpoint = kioskEndpoint;
        this.completionFactory = completionFactory;
        this.agentCallFactory = agentCallFactory;
        this.outputFormatFactory = outputFormatFactory;
        this.chatClient = chatClient;
    }

    // A ModelClient is created per call (one per case × evaluator × baseline/candidate A/B run) and
    // owns the IChatClient it was built with — the OpenAI-backed transport behind it is disposable.
    // Disposing here releases that transport instead of abandoning it. Idempotent so the deterministic
    // using-disposal at the call site and any Autofac scope-end disposal can't double-free.
    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        chatClient.Dispose();
    }

    /// <summary>
    /// Completes asynchronously.
    /// </summary>
    public Task<ICompletion> CompleteAsync(
        Conversation conversation,
        ModelOptions? options = null,
        IReadOnlyDictionary<string, string>? promptVariables = null,
        CancellationToken cancellationToken = default)
    {
        SystemMessage systemMessage = agent.CreateSystemMessage(promptVariables);
        return CompleteAsync(systemMessage, conversation, options, cancellationToken);
    }

    /// <summary>
    /// Completes asynchronously.
    /// </summary>
    public async Task<TypedCompletion<TOutput>> CompleteAsync<TOutput>(
        Conversation conversation,
        ModelOptions? options = null,
        IReadOnlyDictionary<string, string>? promptVariables = null,
        CancellationToken cancellationToken = default)
    {
        IOutputFormat outputFormat = outputFormatFactory(typeof(TOutput));

        SystemMessage systemMessage = agent.CreateSystemMessage(promptVariables);

        systemMessage = new SystemMessage($"""
                                           {systemMessage}
                                           {outputFormat.ToPromptString()}
                                           """);

        ICompletion completion = await CompleteAsync(
            systemMessage,
            conversation,
            options,
            cancellationToken);

        TOutput? output = await outputFormat.ParseAsync<TOutput>(
            completion.Response.GetTextResponse(),
            cancellationToken);

        return new TypedCompletion<TOutput>(output, completion.Usage, completion.Latency);
    }

    /// <summary>
    /// Builds the request preview.
    /// </summary>
    public ModelRequestPreview BuildRequestPreview(
        Conversation conversation,
        ModelOptions? options = null,
        IReadOnlyDictionary<string, string>? promptVariables = null)
    {
        SystemMessage systemMessage = agent.CreateSystemMessage(promptVariables);
        options ??= ModelOptionsFactory.FromAgent(agent, endpoint.Model);
        conversation = Conversation.ReplaceSystemMessage(conversation, systemMessage);

        var messages = conversation.Messages.Select(ToPreviewMessage).ToList();
        var tools = options.Tools
            .Select(t => new RequestToolPreview(t.Name, t.Description, t.Arguments.JsonSchema))
            .ToList();

        return new ModelRequestPreview(endpoint.Model.Name, messages, tools);
    }

    private static RequestMessagePreview ToPreviewMessage(Message message)
    {
        var role = message.Role.ToString().ToLowerInvariant();

        if (message is AssistantMessage { ToolRequests.Count: > 0 } assistant)
        {
            var calls = assistant.ToolRequests
                .Select(tr => new RequestToolCallPreview(tr.Id, tr.Name, tr.Arguments))
                .ToList();
            return new RequestMessagePreview(role, NullIfEmpty(assistant.GetText()), calls, null);
        }

        if (message is ToolMessage toolMessage)
        {
            var (id, contents) = toolMessage.Deconstruct();
            var content = string.Concat(contents.Select(c => c.Text ?? string.Empty));
            return new RequestMessagePreview(role, content, [], id);
        }

        return new RequestMessagePreview(role, NullIfEmpty(message.GetText()), [], null);
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrEmpty(text) ? null : text;

    private async Task<ICompletion> CompleteAsync(
        SystemMessage systemMessage,
        Conversation conversation,
        ModelOptions? options,
        CancellationToken cancellationToken)
    {
        if (kioskOptions.Enabled && !kioskEndpoint.IsConfigured)
        {
            throw new InvalidOperationException(
                "Model calls are disabled in read-only kiosk mode (no LLM endpoint configured). " +
                "This instance of ModelClient is not functional.");
        }
        
        options ??= ModelOptionsFactory.FromAgent(agent, endpoint.Model);
        conversation = Conversation.ReplaceSystemMessage(conversation, systemMessage);

        ICompletion? completion = null;
        Exception? error = null;
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            ChatResponse response = await GetResponseAsync(conversation, options, cancellationToken);

            TimeSpan latency = sw.Elapsed;

            var toolRequests = response
                .Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .Select(fc => new ToolRequest(
                    fc.CallId,
                    fc.Name,
                    fc.Arguments is not null ? JsonSerializer.Serialize(fc.Arguments) : "{}"))
                .ToList();

            var responseContents = new List<Content>();
            if (!string.IsNullOrWhiteSpace(response.Text))
                responseContents.Add(Content.FromText(response.Text));

            var message = Message.CreateAssistantMessage(
                contents: responseContents,
                toolRequests: toolRequests);

            TokenUsage? usage = null;
            if (response.Usage is { InputTokenCount: not null, OutputTokenCount: not null })
            {
                usage = new TokenUsage(
                    (ulong)response.Usage.InputTokenCount.Value,
                    (ulong)response.Usage.OutputTokenCount.Value,
                    (ulong)(response.Usage.CachedInputTokenCount ?? 0));
            }

            completion = completionFactory(message, usage, latency);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-initiated cancellation is not a model failure: rethrow without recording a
            // captured call. Otherwise the cancellation would be persisted as a phantom HTTP-500
            // trace (when !skipIngestion), polluting stats/anomaly data. A provider-side timeout
            // (NetworkTimeout) raises an OperationCanceledException that is NOT from this token, so
            // it correctly falls through to the generic catch and is recorded as a real error.
            throw;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (!skipIngestion)
        {
            HttpStatusCode statusCode = error != null ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
            IAgentCall agentCall = agentCallFactory(agent, agent.CurrentVersion, endpoint, conversation, completion, statusCode);
            await agentCall.AddAsync(cancellationToken);
        }

        if (error is not null)
        {
            // Rethrow via ExceptionDispatchInfo so the original provider-side stack trace survives;
            // a bare `throw error` would reset it to this line and lose the failure's origin.
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return completion
               ?? throw new InvalidOperationException(
                   "Completion is null after successful response. This should not happen.");
    }

    /// <summary>
    /// Sends the completion request, retrying once without the reasoning budget when the provider
    /// rejects that parameter.
    /// </summary>
    /// <remarks>
    /// <c>reasoning_effort</c> only exists on models that have reasoning to constrain: OpenAI
    /// answers it with a 400 on a gpt-4o-class model, and a reasoning model rejects a level it does
    /// not implement (o-series takes low/medium/high, so <c>none</c> is an error there). Callers set
    /// the budget to keep an *internal* feature quick — the operator picked the model, not the
    /// parameter — so the honest answer to "this model has no such knob" is to ask again without
    /// it. The user waits longer; they do not get a broken feature. The retry is deliberately
    /// narrow: only a 400 that names the parameter qualifies, so a genuinely malformed request
    /// still fails once rather than being sent twice.
    /// </remarks>
    private async Task<ChatResponse> GetResponseAsync(
        Conversation conversation,
        ModelOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await chatClient.GetResponseAsync(
                conversation.ToChatMessages(),
                options.ToChatOptions(),
                cancellationToken);
        }
        catch (ClientResultException ex) when (RejectsReasoningEffort(ex, options.Sampling))
        {
            ModelOptions withoutReasoning = options with
            {
                Sampling = options.Sampling is { } sampling
                    ? sampling with { ReasoningEffort = null }
                    : null,
            };

            return await chatClient.GetResponseAsync(
                conversation.ToChatMessages(),
                withoutReasoning.ToChatOptions(),
                cancellationToken);
        }
    }

    private static bool RejectsReasoningEffort(ClientResultException error, ModelSamplingParameters? sampling)
        => sampling?.ReasoningEffort is not null
           && error.Status == (int)HttpStatusCode.BadRequest
           && error.Message.Contains("reasoning", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Stream asynchronously.
    /// </summary>
    public async IAsyncEnumerable<ModelStreamUpdate> StreamAsync(
        SystemMessage systemMessage,
        Conversation conversation,
        ModelOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= ModelOptionsFactory.FromAgent(agent, endpoint.Model);
        conversation = Conversation.ReplaceSystemMessage(conversation, systemMessage);

        Stopwatch sw = Stopwatch.StartNew();
        TokenUsage? usage = null;
        string? finishReason = null;
        var emittedToolCallIds = new HashSet<string>();

        IAsyncEnumerable<ChatResponseUpdate> stream = chatClient.GetStreamingResponseAsync(
            conversation.ToChatMessages(),
            options.ToChatOptions(),
            cancellationToken);

        await foreach (ChatResponseUpdate update in stream)
        {
            foreach (AIContent content in update.Contents)
            {
                if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                {
                    yield return new TextDelta(text.Text);
                }
                else if (content is FunctionCallContent fc)
                {
                    if (string.IsNullOrEmpty(fc.CallId) || !emittedToolCallIds.Add(fc.CallId))
                        continue;

                    var args = fc.Arguments is not null ? JsonSerializer.Serialize(fc.Arguments) : "{}";
                    yield return new ToolRequested(new ToolRequest(fc.CallId, fc.Name, args));
                }
                else if (content is UsageContent { Details: { InputTokenCount: not null, OutputTokenCount: not null } } uc)
                {
                    usage = new TokenUsage(
                        (ulong)uc.Details.InputTokenCount.Value,
                        (ulong)uc.Details.OutputTokenCount.Value,
                        (ulong)(uc.Details.CachedInputTokenCount ?? 0));
                }
            }

            if (update.FinishReason is { } reason)
            {
                finishReason = reason.Value;
            }
        }

        yield return new Completed(usage, sw.Elapsed, finishReason);
    }

    private static IChatClient CreateChatClient(IModelEndpoint endpoint)
    {
        if (endpoint.Provider.Kind is not ModelProviderKind.OpenAi and not ModelProviderKind.OpenAiCompatible)
        {
            throw new NotSupportedException($"Model provider kind {endpoint.Provider.Kind} is not supported");
        }

        var credential = new ApiKeyCredential(endpoint.Provider.ApiKey);
        return new OpenAIClient(credential, BuildClientOptions(endpoint))
            .GetChatClient(endpoint.Model.Name)
            .AsIChatClient();
    }

    // A wedged/slow provider must not be able to pin a worker indefinitely: without these every
    // model call relied entirely on the caller's CancellationToken for an upper time bound, so a
    // hung endpoint could stall the (serial) A/B-validation / optimization queue. NetworkTimeout
    // gives each call a hard ceiling independent of the caller token, and the bounded retry policy
    // recovers from transient upstream failures without retrying forever.
    internal static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(5);
    internal const int MaxRetries = 2;

    internal static OpenAIClientOptions BuildClientOptions(IModelEndpoint endpoint) =>
        new()
        {
            Endpoint = endpoint.Provider.Endpoint,
            NetworkTimeout = NetworkTimeout,
            RetryPolicy = new ClientRetryPolicy(maxRetries: MaxRetries),
        };
}
