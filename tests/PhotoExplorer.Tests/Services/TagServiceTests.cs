using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Services;
using PhotoExplorer.Data;

namespace PhotoExplorer.Tests.Services;

public class TagServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _ctx;

    public TagServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _ctx = new AppDbContext(options);
        _ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddTag_NonJpeg_SavesInDb()
    {
        var svc = new TagService(_ctx);

        await svc.AddTagAsync(@"C:\photo.cr2", "夏");

        var tags = await svc.GetTagsAsync(@"C:\photo.cr2");
        Assert.Contains(tags, t => t.Name == "夏");
    }

    [Fact]
    public async Task RemoveTag_NonJpeg_RemovesFromDb()
    {
        var svc = new TagService(_ctx);
        await svc.AddTagAsync(@"C:\photo.cr2", "夏");

        await svc.RemoveTagAsync(@"C:\photo.cr2", "夏");

        var tags = await svc.GetTagsAsync(@"C:\photo.cr2");
        Assert.Empty(tags);
    }

    [Fact]
    public async Task AddTag_NoDuplicates()
    {
        var svc = new TagService(_ctx);

        await svc.AddTagAsync(@"C:\photo.cr2", "夏");
        await svc.AddTagAsync(@"C:\photo.cr2", "夏");

        var tags = await svc.GetTagsAsync(@"C:\photo.cr2");
        Assert.Single(tags);
    }

    [Fact]
    public async Task GetAllTagNames_ReturnsDistinctNames()
    {
        var svc = new TagService(_ctx);
        await svc.AddTagAsync(@"C:\a.cr2", "夏");
        await svc.AddTagAsync(@"C:\b.cr2", "夏");
        await svc.AddTagAsync(@"C:\c.cr2", "冬");

        var names = await svc.GetAllTagNamesAsync();
        Assert.Equal(2, names.Count);
        Assert.Contains("夏", names);
        Assert.Contains("冬", names);
    }
}
