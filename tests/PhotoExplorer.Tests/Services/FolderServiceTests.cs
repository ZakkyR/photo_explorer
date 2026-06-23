using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Services;
using PhotoExplorer.Data;

namespace PhotoExplorer.Tests.Services;

public class FolderServiceTests
{
    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task RegisterFolder_PersistsToDb()
    {
        using var ctx = CreateContext();
        var svc = new FolderService(ctx);

        await svc.RegisterFolderAsync(@"C:\Photos\Test");

        var folders = await svc.GetRegisteredFoldersAsync();
        Assert.Contains(@"C:\Photos\Test", folders);
    }

    [Fact]
    public async Task RegisterFolder_NoDuplicates()
    {
        using var ctx = CreateContext();
        var svc = new FolderService(ctx);

        await svc.RegisterFolderAsync(@"C:\Photos\Test");
        await svc.RegisterFolderAsync(@"C:\Photos\Test");

        var folders = await svc.GetRegisteredFoldersAsync();
        Assert.Single(folders);
    }

    [Fact]
    public async Task UnregisterFolder_RemovesFromDb()
    {
        using var ctx = CreateContext();
        var svc = new FolderService(ctx);
        await svc.RegisterFolderAsync(@"C:\Photos\Test");

        await svc.UnregisterFolderAsync(@"C:\Photos\Test");

        var folders = await svc.GetRegisteredFoldersAsync();
        Assert.Empty(folders);
    }

    [Fact]
    public void GetImageFilesInFolder_ReturnsOnlySupportedExtensions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "a.jpg"), "");
        File.WriteAllText(Path.Combine(tempDir, "b.png"), "");
        File.WriteAllText(Path.Combine(tempDir, "c.txt"), "");
        try
        {
            using var ctx = CreateContext();
            var svc = new FolderService(ctx);
            var files = svc.GetImageFilesInFolder(tempDir).ToList();
            Assert.Equal(2, files.Count);
            Assert.DoesNotContain(files, f => f.EndsWith(".txt"));
        }
        finally { Directory.Delete(tempDir, true); }
    }
}
