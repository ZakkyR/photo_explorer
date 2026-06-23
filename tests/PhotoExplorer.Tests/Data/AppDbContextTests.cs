using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Data;
using PhotoExplorer.Data.Entities;

namespace PhotoExplorer.Tests.Data;

public class AppDbContextTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CanSaveAndRetrieveFolder()
    {
        using var ctx = CreateContext();
        ctx.Folders.Add(new FolderEntity { Path = @"C:\Photos\Test" });
        await ctx.SaveChangesAsync();

        var saved = await ctx.Folders.FirstAsync();
        Assert.Equal(@"C:\Photos\Test", saved.Path);
    }

    [Fact]
    public async Task CanSaveImageTag()
    {
        using var ctx = CreateContext();
        ctx.ImageTags.Add(new ImageTagEntity { FilePath = @"C:\Photos\img.cr2", TagName = "夏" });
        await ctx.SaveChangesAsync();

        var tag = await ctx.ImageTags.FirstAsync();
        Assert.Equal("夏", tag.TagName);
    }

    [Fact]
    public async Task AlbumFolder_CascadeDeletesWithAlbum()
    {
        using var ctx = CreateContext();
        var album = new AlbumEntity { Name = "Test" };
        album.AlbumFolders.Add(new AlbumFolderEntity { FolderPath = @"C:\Photos" });
        ctx.Albums.Add(album);
        await ctx.SaveChangesAsync();

        ctx.Albums.Remove(album);
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await ctx.AlbumFolders.CountAsync());
    }
}
