using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using PhotoExplorer.Data;

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

public class SidecarServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SidecarServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext()
    {
        var ctx = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteSidecar(string folderPath, SidecarFile file)
    {
        var sidecarDir = Path.Combine(folderPath, ".photoexplorer");
        Directory.CreateDirectory(sidecarDir);
        File.WriteAllText(
            Path.Combine(sidecarDir, "tags.json"),
            JsonSerializer.Serialize(file));
    }

    [Fact]
    public async Task MergeIntoDb_AddsTag_WhenNotRemoved()
    {
        using var ctx = CreateContext();
        var tmpDir = MakeTempDir();
        try
        {
            WriteSidecar(tmpDir, new SidecarFile
            {
                Entries = [new() { File = "img.jpg", Tag = "旅行", Removed = false, Ts = DateTime.UtcNow }]
            });

            using var svc = new SidecarService(ctx);
            await svc.MergeIntoDbAsync(tmpDir);

            Assert.Contains(ctx.ImageTags,
                t => t.FilePath == Path.Combine(tmpDir, "img.jpg") && t.TagName == "旅行");
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    [Fact]
    public async Task MergeIntoDb_RemovesTag_WhenRemoved()
    {
        using var ctx = CreateContext();
        var tmpDir = MakeTempDir();
        try
        {
            ctx.ImageTags.Add(new() { FilePath = Path.Combine(tmpDir, "img.jpg"), TagName = "旅行" });
            await ctx.SaveChangesAsync();

            WriteSidecar(tmpDir, new SidecarFile
            {
                Entries = [new() { File = "img.jpg", Tag = "旅行", Removed = true, Ts = DateTime.UtcNow }]
            });

            using var svc = new SidecarService(ctx);
            await svc.MergeIntoDbAsync(tmpDir);

            Assert.DoesNotContain(ctx.ImageTags,
                t => t.FilePath == Path.Combine(tmpDir, "img.jpg") && t.TagName == "旅行");
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    [Fact]
    public async Task MergeIntoDb_MergesBothSides()
    {
        using var ctx = CreateContext();
        var tmpDir = MakeTempDir();
        try
        {
            ctx.ImageTags.Add(new() { FilePath = Path.Combine(tmpDir, "img.jpg"), TagName = "旅行" });
            await ctx.SaveChangesAsync();

            WriteSidecar(tmpDir, new SidecarFile
            {
                Entries = [new() { File = "img.jpg", Tag = "家族", Removed = false, Ts = DateTime.UtcNow }]
            });

            using var svc = new SidecarService(ctx);
            await svc.MergeIntoDbAsync(tmpDir);

            var tags = ctx.ImageTags
                .Where(t => t.FilePath == Path.Combine(tmpDir, "img.jpg"))
                .Select(t => t.TagName).ToList();
            Assert.Contains("旅行", tags);
            Assert.Contains("家族", tags);
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    [Fact]
    public async Task AddEntryAsync_WritesTagsJson()
    {
        using var ctx = CreateContext();
        var tmpDir = MakeTempDir();
        try
        {
            using var svc = new SidecarService(ctx);
            await svc.AddEntryAsync(Path.Combine(tmpDir, "img.jpg"), "旅行");

            var path = Path.Combine(tmpDir, ".photoexplorer", "tags.json");
            Assert.True(File.Exists(path));
            var file = JsonSerializer.Deserialize<SidecarFile>(File.ReadAllText(path))!;
            Assert.Single(file.Entries);
            Assert.Equal("img.jpg", file.Entries[0].File);
            Assert.False(file.Entries[0].Removed);
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    [Fact]
    public async Task RemoveEntryAsync_MarksRemovedTrue()
    {
        using var ctx = CreateContext();
        var tmpDir = MakeTempDir();
        try
        {
            using var svc = new SidecarService(ctx);
            await svc.AddEntryAsync(Path.Combine(tmpDir, "img.jpg"), "旅行");
            await svc.RemoveEntryAsync(Path.Combine(tmpDir, "img.jpg"), "旅行");

            var path = Path.Combine(tmpDir, ".photoexplorer", "tags.json");
            var file = JsonSerializer.Deserialize<SidecarFile>(File.ReadAllText(path))!;
            var latest = file.GetLatestEntries();
            Assert.Single(latest);
            Assert.True(latest[0].Removed);
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    [Fact]
    public async Task WriteInitialTagsAsync_SkipsIfTagsJsonExists()
    {
        using var ctx = CreateContext();
        var tmpDir = MakeTempDir();
        try
        {
            // 既存の tags.json を作成
            WriteSidecar(tmpDir, new SidecarFile
            {
                Entries = [new() { File = "img.jpg", Tag = "既存", Removed = false, Ts = DateTime.UtcNow }]
            });

            using var svc = new SidecarService(ctx);
            await svc.WriteInitialTagsAsync(tmpDir, [("img.jpg", "新規")]);

            // 既存ファイルが上書きされていないこと
            var path = Path.Combine(tmpDir, ".photoexplorer", "tags.json");
            var file = JsonSerializer.Deserialize<SidecarFile>(File.ReadAllText(path))!;
            Assert.Contains(file.Entries, e => e.Tag == "既存");
            Assert.DoesNotContain(file.Entries, e => e.Tag == "新規");
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }
}
