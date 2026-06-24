using PhotoExplorer.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PhotoExplorer.Core.Services;

public class ImageService : IImageService
{
    private readonly IFolderService _folderService;

    public ImageService(IFolderService folderService) => _folderService = folderService;

    public async Task<IReadOnlyList<ImageItem>> LoadImagesFromFolderAsync(
        string folderPath, ITagService tagService)
    {
        var files = _folderService.GetImageFilesInFolder(folderPath).ToList();
        var tagsBulk = await tagService.GetTagsBulkAsync(files);
        return files.Select(f => new ImageItem(f)
        {
            Tags = tagsBulk.TryGetValue(f, out var t) ? t : new List<Tag>()
        }).ToList();
    }

    public async Task<IReadOnlyList<ImageItem>> LoadImagesFromAlbumAsync(
        Album album, ITagService tagService)
    {
        var items = new List<ImageItem>();
        foreach (var path in album.FolderPaths)
            items.AddRange(await LoadImagesFromFolderAsync(path, tagService));
        return items;
    }

    public async Task<byte[]?> GenerateThumbnailAsync(string filePath, int maxSize = 200)
    {
        try
        {
            using var image = await Image.LoadAsync(filePath);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxSize, maxSize),
                Mode = ResizeMode.Max
            }));
            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
