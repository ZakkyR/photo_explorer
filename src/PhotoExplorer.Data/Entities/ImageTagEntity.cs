namespace PhotoExplorer.Data.Entities;

public class ImageTagEntity
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
}
