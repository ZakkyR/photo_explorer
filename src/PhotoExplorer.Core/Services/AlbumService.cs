using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Data;
using PhotoExplorer.Data.Entities;

namespace PhotoExplorer.Core.Services;

public class AlbumService : IAlbumService
{
    private readonly AppDbContext _ctx;

    public AlbumService(AppDbContext ctx) => _ctx = ctx;

    public async Task<Album> CreateAlbumAsync(string name)
    {
        var entity = new AlbumEntity { Name = name };
        _ctx.Albums.Add(entity);
        await _ctx.SaveChangesAsync();
        return new Album { Id = entity.Id, Name = entity.Name };
    }

    public async Task DeleteAlbumAsync(int albumId)
    {
        var entity = await _ctx.Albums
            .Include(a => a.AlbumFolders)
            .FirstOrDefaultAsync(a => a.Id == albumId);
        if (entity != null) { _ctx.Albums.Remove(entity); await _ctx.SaveChangesAsync(); }
    }

    public async Task<IReadOnlyList<Album>> GetAlbumsAsync()
        => await _ctx.Albums
            .Include(a => a.AlbumFolders)
            .Select(a => new Album
            {
                Id = a.Id,
                Name = a.Name,
                FolderPaths = a.AlbumFolders.Select(af => af.FolderPath).ToList()
            })
            .ToListAsync();

    public async Task AddFolderToAlbumAsync(int albumId, string folderPath)
    {
        if (!await _ctx.AlbumFolders.AnyAsync(af => af.AlbumId == albumId && af.FolderPath == folderPath))
        {
            _ctx.AlbumFolders.Add(new AlbumFolderEntity { AlbumId = albumId, FolderPath = folderPath });
            await _ctx.SaveChangesAsync();
        }
    }

    public async Task RemoveFolderFromAlbumAsync(int albumId, string folderPath)
    {
        var entity = await _ctx.AlbumFolders
            .FirstOrDefaultAsync(af => af.AlbumId == albumId && af.FolderPath == folderPath);
        if (entity != null) { _ctx.AlbumFolders.Remove(entity); await _ctx.SaveChangesAsync(); }
    }
}
