using PhotoExplorer.Core.Models;

namespace PhotoExplorer.Core.Services;

public interface IImageService
{
    Task<IReadOnlyList<ImageItem>> LoadImagesFromFolderAsync(string folderPath, ITagService tagService);
    Task<IReadOnlyList<ImageItem>> LoadImagesFromAlbumAsync(Album album, ITagService tagService);
    Task<byte[]?> GenerateThumbnailAsync(string filePath, int maxSize = 200);
}
