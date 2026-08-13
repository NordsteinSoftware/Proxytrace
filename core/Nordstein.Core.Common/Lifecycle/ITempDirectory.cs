namespace Nordstein.Core.Common.Lifecycle;

public interface ITempDirectory : IDisposable
{
    delegate ITempDirectory Create(
        string? parentDirectory = null,
        string? prefix = null);
    
    string Path { get; }

    string Combine(string path);
}