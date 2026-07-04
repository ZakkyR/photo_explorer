using System.Text.Json.Serialization;

namespace PhotoExplorer.Core.Models;

public class SidecarEntry
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";

    [JsonPropertyName("removed")]
    public bool Removed { get; set; }

    [JsonPropertyName("ts")]
    public DateTime Ts { get; set; }
}
