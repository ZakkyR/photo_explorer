namespace PhotoExplorer.Core.Services;

public class FolderChangedEventArgs : EventArgs
{
    public string FolderPath { get; }
    public string FilePath { get; }
    public WatcherChangeTypes ChangeType { get; }

    public FolderChangedEventArgs(string folderPath, string filePath, WatcherChangeTypes changeType)
    {
        FolderPath = folderPath;
        FilePath = filePath;
        ChangeType = changeType;
    }
}

public interface IFolderService : IDisposable
{
    Task RegisterFolderAsync(string path);
    Task UnregisterFolderAsync(string path);
    Task<IReadOnlyList<string>> GetRegisteredFoldersAsync();
    IEnumerable<string> GetImageFilesInFolder(string folderPath);
    event EventHandler<FolderChangedEventArgs>? FolderChanged;
}
