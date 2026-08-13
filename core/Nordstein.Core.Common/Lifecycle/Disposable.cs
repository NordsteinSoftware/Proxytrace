namespace Nordstein.Core.Common.Lifecycle;

public class Disposable : IDisposable, IAsyncDisposable
{
    private readonly Func<ValueTask>? asyncAction;
    private readonly Action? action;
    private bool isDisposed;

    public Disposable(Action action)
    {
        this.action = action;
    }

    public Disposable(Func<ValueTask> asyncAction)
    {
        this.asyncAction = asyncAction;
    }
    
    public static IAsyncDisposable Create(Func<ValueTask> asyncAction)
        => new Disposable(asyncAction);
    
    public static IDisposable Create(Action action)
        => new Disposable(action);
    
    public void Dispose()
    {
        if(isDisposed)
        {
            return;
        }

        action?.Invoke();
        _ = asyncAction?.Invoke();
        isDisposed = true;
        
    }

    public async ValueTask DisposeAsync()
    {
        if(isDisposed)
        {
            return;
        }

        try
        {
            if (asyncAction != null)
            {
                await asyncAction.Invoke();
            }
        }
        catch
        {
            // ignored
        }

        action?.Invoke();
        isDisposed = true;
    }
}