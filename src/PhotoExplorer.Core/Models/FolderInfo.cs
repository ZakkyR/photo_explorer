namespace PhotoExplorer.Core.Models;

public record FolderInfo(string Path, string? DisplayName)
{
    public string DisplayLabel => DisplayName ?? System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
}
