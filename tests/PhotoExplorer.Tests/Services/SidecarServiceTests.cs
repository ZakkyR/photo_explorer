using System.Text.Json;
using PhotoExplorer.Core.Models;

namespace PhotoExplorer.Tests.Services;

public class SidecarModelTests
{
    [Fact]
    public void SidecarFile_RoundTrips_Json()
    {
        var file = new SidecarFile
        {
            Entries =
            [
                new() { File = "img.jpg", Tag = "旅行", Removed = false, Ts = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc) }
            ]
        };
        var json = JsonSerializer.Serialize(file);
        var result = JsonSerializer.Deserialize<SidecarFile>(json);
        Assert.NotNull(result);
        Assert.Single(result.Entries);
        Assert.Equal("img.jpg", result.Entries[0].File);
        Assert.Equal("旅行", result.Entries[0].Tag);
        Assert.False(result.Entries[0].Removed);
    }

    [Fact]
    public void GetLatestEntries_PrefersMostRecentTs()
    {
        var file = new SidecarFile
        {
            Entries =
            [
                new() { File = "img.jpg", Tag = "旅行", Removed = false, Ts = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc) },
                new() { File = "img.jpg", Tag = "旅行", Removed = true,  Ts = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc) }
            ]
        };
        var latest = file.GetLatestEntries();
        Assert.Single(latest);
        Assert.True(latest[0].Removed);
    }

    [Fact]
    public void GetLatestEntries_CaseInsensitiveDedup()
    {
        var file = new SidecarFile
        {
            Entries =
            [
                new() { File = "IMG.JPG", Tag = "旅行", Removed = false, Ts = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc) },
                new() { File = "img.jpg", Tag = "旅行", Removed = true,  Ts = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc) }
            ]
        };
        var latest = file.GetLatestEntries();
        Assert.Single(latest);
    }
}
