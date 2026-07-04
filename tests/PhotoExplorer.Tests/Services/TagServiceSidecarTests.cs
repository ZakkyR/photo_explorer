using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Services;
using PhotoExplorer.Data;

namespace PhotoExplorer.Tests.Services;

/// <summary>
/// TagService + ISidecarService 統合テスト。
/// SidecarService の実装（SQLite 接続）を使い、サイドカー呼び出しが
/// AddTagAsync / AddTagBulkAsync / RemoveTagAsync / RemoveTagBulkAsync から
/// 正しく行われることを確認する。
/// </summary>
public class TagServiceSidecarTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _tmpDir;

    public TagServiceSidecarTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
    }

    private AppDbContext CreateContext()
    {
        var ctx = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    // サイドカー tags.json のパス
    private string SidecarPath => Path.Combine(_tmpDir, ".photoexplorer", "tags.json");

    [Fact]
    public async Task AddTagAsync_WithSidecar_WritesSidecarEntry()
    {
        using var ctx = CreateContext();
        using var sidecar = new SidecarService(ctx);
        var svc = new TagService(ctx, sidecar);

        var filePath = Path.Combine(_tmpDir, "photo.cr2");
        await svc.AddTagAsync(filePath, "夏");

        Assert.True(File.Exists(SidecarPath));
    }

    [Fact]
    public async Task AddTagBulkAsync_WithSidecar_WritesSidecarEntries()
    {
        using var ctx = CreateContext();
        using var sidecar = new SidecarService(ctx);
        var svc = new TagService(ctx, sidecar);

        var files = new List<string>
        {
            Path.Combine(_tmpDir, "a.cr2"),
            Path.Combine(_tmpDir, "b.cr2")
        };
        await svc.AddTagBulkAsync(files, "夏");

        Assert.True(File.Exists(SidecarPath));
    }

    [Fact]
    public async Task RemoveTagAsync_WithSidecar_WritesRemovedEntry()
    {
        using var ctx = CreateContext();
        using var sidecar = new SidecarService(ctx);
        var svc = new TagService(ctx, sidecar);

        var filePath = Path.Combine(_tmpDir, "photo.cr2");
        await svc.AddTagAsync(filePath, "夏");
        await svc.RemoveTagAsync(filePath, "夏");

        Assert.True(File.Exists(SidecarPath));
    }

    [Fact]
    public async Task RemoveTagBulkAsync_WithSidecar_WritesSidecarEntries()
    {
        using var ctx = CreateContext();
        using var sidecar = new SidecarService(ctx);
        var svc = new TagService(ctx, sidecar);

        var files = new List<string>
        {
            Path.Combine(_tmpDir, "a.cr2"),
            Path.Combine(_tmpDir, "b.cr2")
        };
        await svc.AddTagBulkAsync(files, "冬");
        await svc.RemoveTagBulkAsync(files, "冬");

        Assert.True(File.Exists(SidecarPath));
    }

    [Fact]
    public async Task TagService_WithoutSidecar_DoesNotThrow()
    {
        // sidecar = null でも既存コードが壊れないことを確認
        using var ctx = CreateContext();
        var svc = new TagService(ctx); // sidecar 省略

        var filePath = Path.Combine(_tmpDir, "photo.cr2");
        // null sidecar でも例外が発生しないこと
        await svc.AddTagAsync(filePath, "夏");
        await svc.RemoveTagAsync(filePath, "夏");
    }
}
