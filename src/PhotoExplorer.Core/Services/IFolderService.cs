using PhotoExplorer.Core.Models;

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
    Task<IReadOnlyList<FolderInfo>> GetRegisteredFoldersAsync();
    Task RenameFolderAsync(string folderPath, string displayName);
    Task<string?> GetDisplayNameAsync(string folderPath);
    IEnumerable<string> GetImageFilesInFolder(string folderPath);
    event EventHandler<FolderChangedEventArgs>? FolderChanged;
}
