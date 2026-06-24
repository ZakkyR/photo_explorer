using PhotoExplorer.Core.Models;

namespace PhotoExplorer.Core.Services;

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetTagsAsync(string filePath);
    Task<Dictionary<string, List<Tag>>> GetTagsBulkAsync(IReadOnlyList<string> filePaths);
    Task AddTagAsync(string filePath, string tagName);
    Task AddTagBulkAsync(IReadOnlyList<string> filePaths, string tagName);
    Task RemoveTagAsync(string filePath, string tagName);
    Task RemoveTagBulkAsync(IReadOnlyList<string> filePaths, string tagName);
    Task<IReadOnlyList<string>> GetAllTagNamesAsync();
}
