namespace PhotoExplorer.Core.Models;

public class ImageItem
{
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
    public List<Tag> Tags { get; set; } = new();
    public byte[]? ThumbnailBytes { get; set; }

    public ImageItem(string filePath) => FilePath = filePath;
}
