namespace PhotoExplorer.Data.Entities;

public class FolderEntity
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
