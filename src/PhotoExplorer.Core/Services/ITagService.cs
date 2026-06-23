using PhotoExplorer.Core.Models;

namespace PhotoExplorer.Core.Services;

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetTagsAsync(string filePath);
    Task AddTagAsync(string filePath, string tagName);
    Task RemoveTagAsync(string filePath, string tagName);
    Task<IReadOnlyList<string>> GetAllTagNamesAsync();
}
