using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;

namespace Proxytrace.Domain.ModelEndpoint;

/// <summary>
/// Extensions for <see cref="IModelClient"/>
/// </summary>
public static class ModelClientExtensions
{
    public static Task<ICompletion> CompleteAsync(
        this IModelClient client,
        UserMessage userMessage,
        ModelOptions? modelOptions = null,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Conversation conversation = Conversation.Create().With(userMessage);
        return client.CompleteAsync(
            conversation, 
            modelOptions,
            variables,
            cancellationToken);
    }

    public static async Task<TOutput?> CompleteAsync<TOutput>(
        this IModelClient client,
        UserMessage userMessage,
        ModelOptions? modelOptions = null,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Conversation conversation = Conversation.Create().With(userMessage);
        var completion = await client.CompleteAsync<TOutput>(
            conversation, 
            modelOptions, 
            variables,
            cancellationToken);
        return completion.Response;
    }
}
