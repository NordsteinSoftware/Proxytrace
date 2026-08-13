namespace Nordstein.Core.Common.Async;

public interface IAsyncLock
{
    IDisposable Lock(object key);
    Task<IDisposable> LockAsync(object key, CancellationToken cancellationToken = default);
}