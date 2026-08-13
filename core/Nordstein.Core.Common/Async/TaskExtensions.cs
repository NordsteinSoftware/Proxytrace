namespace Nordstein.Core.Common.Async;

public static class TaskExtensions
{
    public static Task<T> ToTaskResult<T>(this T value)
        => Task.FromResult(value);
    
    public static async Task<IReadOnlyCollection<T>> Await<T>(this IEnumerable<Task<T>> tasks)
        => await Task.WhenAll(tasks);

    public static TResult SynchronouslyAwait<TResult>(this Task<TResult> task)
        => task.ConfigureAwait(false).GetAwaiter().GetResult();
    
    public static TResult SynchronouslyAwait<TResult>(this ValueTask<TResult> task)
        => task.ConfigureAwait(false).GetAwaiter().GetResult();
}