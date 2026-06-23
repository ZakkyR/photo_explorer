namespace PhotoExplorer.Data.Entities;

public class AlbumEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<AlbumFolderEntity> AlbumFolders { get; set; } = new();
}
