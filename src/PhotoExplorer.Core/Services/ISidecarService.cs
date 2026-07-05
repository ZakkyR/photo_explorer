namespace PhotoExplorer.Core.Services;

public interface ISidecarService : IDisposable
{
    Task MergeIntoDbAsync(string folderPath);
    Task ExportToSidecarAsync(string folderPath);
    Task ForceImportFromSidecarAsync(string folderPath);
    Task AddEntryAsync(string filePath, string tagName);
    Task AddEntryBulkAsync(IReadOnlyList<string> filePaths, string tagName);
    Task RemoveEntryAsync(string filePath, string tagName);
    Task RemoveEntryBulkAsync(IReadOnlyList<string> filePaths, string tagName);
    Task WriteInitialTagsAsync(string folderPath, IReadOnlyList<(string fileName, string tagName)> tags);
    void StartWatching(string folderPath, Func<string, Task> onChanged);
    void StopWatching(string folderPath);
}
