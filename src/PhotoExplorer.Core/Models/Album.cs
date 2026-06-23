namespace PhotoExplorer.Core.Models;

public class Album
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> FolderPaths { get; set; } = new();
}
