using System.Text.Json.Serialization;

namespace PhotoExplorer.Core.Models;

public class SidecarFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("entries")]
    public List<SidecarEntry> Entries { get; set; } = [];

    public List<SidecarEntry> GetLatestEntries()
        => Entries
            .GroupBy(e => (
                File: e.File.ToUpperInvariant(),
                Tag: e.Tag.ToUpperInvariant()))
            .Select(g => g.OrderByDescending(e => e.Ts).First())
            .ToList();
}
