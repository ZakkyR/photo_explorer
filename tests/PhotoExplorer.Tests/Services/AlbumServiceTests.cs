using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Services;
using PhotoExplorer.Data;

namespace PhotoExplorer.Tests.Services;

public class AlbumServiceTests
{
    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task CreateAlbum_PersistsAndReturns()
    {
        using var ctx = CreateContext();
        var svc = new AlbumService(ctx);

        var album = await svc.CreateAlbumAsync("夏の思い出");

        Assert.Equal("夏の思い出", album.Name);
        Assert.True(album.Id > 0);
    }

    [Fact]
    public async Task AddFolderToAlbum_AppearsInGetAlbums()
    {
        using var ctx = CreateContext();
        var svc = new AlbumService(ctx);
        var album = await svc.CreateAlbumAsync("Test");

        await svc.AddFolderToAlbumAsync(album.Id, @"C:\Photos");

        var albums = await svc.GetAlbumsAsync();
        Assert.Contains(@"C:\Photos", albums[0].FolderPaths);
    }

    [Fact]
    public async Task DeleteAlbum_RemovesFromDb()
    {
        using var ctx = CreateContext();
        var svc = new AlbumService(ctx);
        var album = await svc.CreateAlbumAsync("Test");

        await svc.DeleteAlbumAsync(album.Id);

        Assert.Empty(await svc.GetAlbumsAsync());
    }
}
