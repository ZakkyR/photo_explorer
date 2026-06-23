using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Data;
using PhotoExplorer.Data.Entities;

namespace PhotoExplorer.Core.Services;

public class FolderService : IFolderService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tiff", ".tif",
            ".raw", ".cr2", ".nef", ".arw", ".dng"
        };

    private readonly AppDbContext _ctx;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();

    public event EventHandler<FolderChangedEventArgs>? FolderChanged;

    public FolderService(AppDbContext ctx) => _ctx = ctx;

    public async Task RegisterFolderAsync(string path)
    {
        if (!await _ctx.Folders.AnyAsync(f => f.Path == path))
        {
            _ctx.Folders.Add(new FolderEntity { Path = path });
            await _ctx.SaveChangesAsync();
        }
        StartWatcher(path);
    }

    public async Task UnregisterFolderAsync(string path)
    {
        var entity = await _ctx.Folders.FirstOrDefaultAsync(f => f.Path == path);
        if (entity != null)
        {
            _ctx.Folders.Remove(entity);
            await _ctx.SaveChangesAsync();
        }
        StopWatcher(path);
    }

    public async Task<IReadOnlyList<FolderInfo>> GetRegisteredFoldersAsync()
        => await _ctx.Folders.Select(f => new FolderInfo(f.Path, f.DisplayName)).ToListAsync();

    public async Task RenameFolderAsync(string folderPath, string displayName)
    {
        var entity = await _ctx.Folders.FirstOrDefaultAsync(f => f.Path == folderPath);
        if (entity != null)
        {
            entity.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
            await _ctx.SaveChangesAsync();
        }
    }

    public async Task<string?> GetDisplayNameAsync(string folderPath)
    {
        var entity = await _ctx.Folders.FirstOrDefaultAsync(f => f.Path == folderPath);
        return entity?.DisplayName;
    }

    public IEnumerable<string> GetImageFilesInFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(folderPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)));
    }

    private void StartWatcher(string path)
    {
        if (!Directory.Exists(path) || _watchers.ContainsKey(path)) return;
        var watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        watcher.Created += (_, e) => Raise(path, e.FullPath, WatcherChangeTypes.Created);
        watcher.Deleted += (_, e) => Raise(path, e.FullPath, WatcherChangeTypes.Deleted);
        watcher.Renamed += (_, e) => Raise(path, e.FullPath, WatcherChangeTypes.Renamed);
        _watchers[path] = watcher;
    }

    private void StopWatcher(string path)
    {
        if (_watchers.TryGetValue(path, out var w)) { w.Dispose(); _watchers.Remove(path); }
    }

    private void Raise(string folderPath, string filePath, WatcherChangeTypes type)
    {
        if (SupportedExtensions.Contains(Path.GetExtension(filePath)))
            FolderChanged?.Invoke(this, new FolderChangedEventArgs(folderPath, filePath, type));
    }

    public void Dispose()
    {
        foreach (var w in _watchers.Values) w.Dispose();
        _watchers.Clear();
    }
}
