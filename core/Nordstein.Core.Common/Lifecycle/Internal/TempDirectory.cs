namespace Nordstein.Core.Common.Lifecycle.Internal;

internal class TempDirectory : ITempDirectory
{
    public string Path { get; }

    public TempDirectory(
        string? parentDirectory = null,
        string? prefix = null)
    {
        parentDirectory ??= System.IO.Path.GetTempPath();
        
        string folderName = !string.IsNullOrWhiteSpace(prefix)
            ? $"{prefix}_{Guid.NewGuid()}"
            : System.IO.Path.GetRandomFileName();
        
        Path = System.IO.Path.Combine(parentDirectory, folderName);
        Directory.CreateDirectory(Path);
    }
    
    public string Combine(string path)
        => System.IO.Path.Combine(Path, path);
    
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}