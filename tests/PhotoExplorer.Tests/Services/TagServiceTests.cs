using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Services;
using PhotoExplorer.Data;

namespace PhotoExplorer.Tests.Services;

public class TagServiceTests
{
    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task AddTag_NonJpeg_SavesInDb()
    {
        using var ctx = CreateContext();
        var svc = new TagService(ctx);

        await svc.AddTagAsync(@"C:\photo.cr2", "夏");

        var tags = await svc.GetTagsAsync(@"C:\photo.cr2");
        Assert.Contains(tags, t => t.Name == "夏");
    }

    [Fact]
    public async Task RemoveTag_NonJpeg_RemovesFromDb()
    {
        using var ctx = CreateContext();
        var svc = new TagService(ctx);
        await svc.AddTagAsync(@"C:\photo.cr2", "夏");

        await svc.RemoveTagAsync(@"C:\photo.cr2", "夏");

        var tags = await svc.GetTagsAsync(@"C:\photo.cr2");
        Assert.Empty(tags);
    }

    [Fact]
    public async Task AddTag_NoDuplicates()
    {
        using var ctx = CreateContext();
        var svc = new TagService(ctx);

        await svc.AddTagAsync(@"C:\photo.cr2", "夏");
        await svc.AddTagAsync(@"C:\photo.cr2", "夏");

        var tags = await svc.GetTagsAsync(@"C:\photo.cr2");
        Assert.Single(tags);
    }

    [Fact]
    public async Task GetAllTagNames_ReturnsDistinctNames()
    {
        using var ctx = CreateContext();
        var svc = new TagService(ctx);
        await svc.AddTagAsync(@"C:\a.cr2", "夏");
        await svc.AddTagAsync(@"C:\b.cr2", "夏");
        await svc.AddTagAsync(@"C:\c.cr2", "冬");

        var names = await svc.GetAllTagNamesAsync();
        Assert.Equal(2, names.Count);
        Assert.Contains("夏", names);
        Assert.Contains("冬", names);
    }
}
