using PhotoExplorer.Core.Models;

namespace PhotoExplorer.Core.Services;

public interface IAlbumService
{
    Task<Album> CreateAlbumAsync(string name);
    Task DeleteAlbumAsync(int albumId);
    Task<IReadOnlyList<Album>> GetAlbumsAsync();
    Task AddFolderToAlbumAsync(int albumId, string folderPath);
    Task RemoveFolderFromAlbumAsync(int albumId, string folderPath);
}
