namespace PhotoExplorer.Core.Models;

public class Tag
{
    public string Name { get; }
    public Tag(string name) => Name = name.Trim();
    public override string ToString() => Name;
    public override bool Equals(object? obj) => obj is Tag t && t.Name == Name;
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
}
