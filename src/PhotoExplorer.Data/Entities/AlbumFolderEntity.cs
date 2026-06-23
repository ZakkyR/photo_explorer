namespace PhotoExplorer.Data.Entities;

public class AlbumFolderEntity
{
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public AlbumEntity Album { get; set; } = null!;
    public string FolderPath { get; set; } = string.Empty;
}
