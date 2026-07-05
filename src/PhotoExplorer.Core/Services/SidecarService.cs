using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Data;
using PhotoExplorer.Data.Entities;

namespace PhotoExplorer.Core.Services;

public class SidecarService : ISidecarService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private const string SidecarDirName = ".photoexplorer";
    private const string SidecarFileName = "tags.json";

    private readonly AppDbContext _ctx;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string, Task>> _callbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _mergedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public SidecarService(AppDbContext ctx) => _ctx = ctx;

    // ── ファイルパスヘルパー ──────────────────────────────────────

    private static string SidecarDir(string folderPath)
        => Path.Combine(folderPath, SidecarDirName);

    private static string SidecarPath(string folderPath)
        => Path.Combine(folderPath, SidecarDirName, SidecarFileName);

    private static readonly string MergedAtDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PhotoExplorer", "merged_at");

    // PC ごとのローカル AppData に保存する（OneDrive に同期させない）
    private static string MergedAtPath(string folderPath)
    {
        Directory.CreateDirectory(MergedAtDir);
        var hash = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(folderPath.ToUpperInvariant())));
        return Path.Combine(MergedAtDir, $"{hash}.ts");
    }

    private static DateTime ReadPersistedMergedAt(string folderPath)
    {
        try
        {
            var p = MergedAtPath(folderPath);
            if (!File.Exists(p)) return DateTime.MinValue;
            return DateTime.Parse(File.ReadAllText(p), null,
                System.Globalization.DateTimeStyles.RoundtripKind);
        }
        catch { return DateTime.MinValue; }
    }

    private void PersistMergedAt(string folderPath, DateTime mtime)
    {
        try { File.WriteAllText(MergedAtPath(folderPath), mtime.ToString("O")); }
        catch { }
        lock (_lock) _mergedAt[folderPath] = mtime;
    }

    // ── JSON 読み書き ────────────────────────────────────────────

    private static SidecarFile ReadFile(string folderPath)
    {
        var path = SidecarPath(folderPath);
        if (!File.Exists(path)) return new();
        try
        {
            return JsonSerializer.Deserialize<SidecarFile>(
                File.ReadAllText(path), JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    private static void WriteFile(string folderPath, SidecarFile file)
    {
        var dir = SidecarDir(folderPath);
        Directory.CreateDirectory(dir);
        var dirInfo = new DirectoryInfo(dir);
        if (!dirInfo.Attributes.HasFlag(FileAttributes.Hidden))
            dirInfo.Attributes |= FileAttributes.Hidden;

        var path = SidecarPath(folderPath);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOpts));
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    // ── コア操作 ─────────────────────────────────────────────────

    public async Task MergeIntoDbAsync(string folderPath)
    {
        var sidecarPath = SidecarPath(folderPath);
        if (!File.Exists(sidecarPath)) return;

        var sidecarMtime = File.GetLastWriteTimeUtc(sidecarPath);

        // in-memory キャッシュチェック（同セッション内の重複 merge をスキップ）
        lock (_lock)
        {
            if (_mergedAt.TryGetValue(folderPath, out var cached) && sidecarMtime <= cached)
                return;
        }

        // 永続化キャッシュチェック（起動時のスキップ）
        var persisted = ReadPersistedMergedAt(folderPath);
        if (sidecarMtime <= persisted)
        {
            lock (_lock) _mergedAt[folderPath] = persisted;
            return;
        }

        var sidecar = ReadFile(folderPath);
        var latest = sidecar.GetLatestEntries();

        if (latest.Count > 0)
        {
            await using var tx = await _ctx.Database.BeginTransactionAsync();
            foreach (var entry in latest)
            {
                var fullPath = Path.Combine(folderPath, entry.File);
                if (entry.Removed)
                    await _ctx.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM ImageTags WHERE FilePath = {fullPath} AND TagName = {entry.Tag}");
                else
                    await _ctx.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO ImageTags (FilePath, TagName) SELECT {fullPath}, {entry.Tag} WHERE NOT EXISTS (SELECT 1 FROM ImageTags WHERE FilePath = {fullPath} AND TagName = {entry.Tag})");
            }
            await tx.CommitAsync();
        }

        PersistMergedAt(folderPath, sidecarMtime);
    }

    public Task AddEntryAsync(string filePath, string tagName)
        => Task.Run(() => UpdateSidecar(
            Path.GetDirectoryName(filePath)!,
            Path.GetFileName(filePath),
            tagName, removed: false));

    public Task AddEntryBulkAsync(IReadOnlyList<string> filePaths, string tagName)
        => Task.Run(() =>
        {
            foreach (var group in filePaths.GroupBy(
                f => Path.GetDirectoryName(f)!, StringComparer.OrdinalIgnoreCase))
            {
                var sidecar = ReadFile(group.Key);
                foreach (var fp in group)
                {
                    var fileName = Path.GetFileName(fp);
                    RemoveExisting(sidecar, fileName, tagName);
                    sidecar.Entries.Add(new() { File = fileName, Tag = tagName, Removed = false, Ts = DateTime.UtcNow });
                }
                WriteFileAndMarkMerged(group.Key, sidecar);
            }
        });

    public Task RemoveEntryAsync(string filePath, string tagName)
        => Task.Run(() => UpdateSidecar(
            Path.GetDirectoryName(filePath)!,
            Path.GetFileName(filePath),
            tagName, removed: true));

    public Task RemoveEntryBulkAsync(IReadOnlyList<string> filePaths, string tagName)
        => Task.Run(() =>
        {
            foreach (var group in filePaths.GroupBy(
                f => Path.GetDirectoryName(f)!, StringComparer.OrdinalIgnoreCase))
            {
                var sidecar = ReadFile(group.Key);
                foreach (var fp in group)
                {
                    var fileName = Path.GetFileName(fp);
                    RemoveExisting(sidecar, fileName, tagName);
                    sidecar.Entries.Add(new() { File = fileName, Tag = tagName, Removed = true, Ts = DateTime.UtcNow });
                }
                WriteFileAndMarkMerged(group.Key, sidecar);
            }
        });

    public Task WriteInitialTagsAsync(string folderPath, IReadOnlyList<(string fileName, string tagName)> tags)
        => Task.Run(() =>
        {
            if (File.Exists(SidecarPath(folderPath))) return;
            var sidecar = new SidecarFile();
            var ts = DateTime.UtcNow;
            foreach (var (fileName, tagName) in tags)
                sidecar.Entries.Add(new() { File = fileName, Tag = tagName, Removed = false, Ts = ts });
            WriteFile(folderPath, sidecar);
        });

    // ── FileSystemWatcher（Task 3 で実装） ───────────────────────

    public void StartWatching(string folderPath, Func<string, Task> onChanged)
    {
        lock (_lock)
        {
            StopWatchingCore(folderPath);
            if (!Directory.Exists(folderPath)) return;
            _callbacks[folderPath] = onChanged;
            var watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                Filter = "*.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            watcher.Changed += (_, e) => OnJsonFileChanged(folderPath, e.FullPath);
            watcher.Created += (_, e) => OnJsonFileChanged(folderPath, e.FullPath);
            watcher.EnableRaisingEvents = true;
            _watchers[folderPath] = watcher;
        }
    }

    public void StopWatching(string folderPath)
    {
        lock (_lock) { StopWatchingCore(folderPath); }
    }

    private void StopWatchingCore(string folderPath)
    {
        if (_watchers.Remove(folderPath, out var w)) { w.EnableRaisingEvents = false; w.Dispose(); }
        _callbacks.Remove(folderPath);
    }

    private void OnJsonFileChanged(string folderPath, string changedPath)
    {
        var sidecarDir = SidecarDir(folderPath);
        if (!changedPath.StartsWith(sidecarDir, StringComparison.OrdinalIgnoreCase)) return;

        var fileName = Path.GetFileName(changedPath);
        if (string.Equals(fileName, SidecarFileName, StringComparison.OrdinalIgnoreCase))
        {
            lock (_lock)
            {
                if (_callbacks.TryGetValue(folderPath, out var cb))
                    _ = cb(folderPath);
            }
        }
        else if (fileName.StartsWith("tags", StringComparison.OrdinalIgnoreCase))
        {
            _ = HandleConflictFileAsync(folderPath, changedPath);
        }
    }

    private async Task HandleConflictFileAsync(string folderPath, string conflictPath)
    {
        try
        {
            await Task.Delay(500);
            if (!File.Exists(conflictPath)) return;

            var conflictJson = await File.ReadAllTextAsync(conflictPath);
            var conflict = JsonSerializer.Deserialize<SidecarFile>(conflictJson, JsonOpts) ?? new();
            if (conflict.Entries.Count == 0) { TryDelete(conflictPath); return; }

            var main = ReadFile(folderPath);
            main.Entries.AddRange(conflict.Entries);
            main.Entries = main.GetLatestEntries();
            WriteFile(folderPath, main);
            TryDelete(conflictPath);

            lock (_lock)
            {
                if (_callbacks.TryGetValue(folderPath, out var cb))
                    _ = cb(folderPath);
            }
        }
        catch { }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    // ── ヘルパー ─────────────────────────────────────────────────

    private void UpdateSidecar(string folderPath, string fileName, string tagName, bool removed)
    {
        var sidecar = ReadFile(folderPath);
        RemoveExisting(sidecar, fileName, tagName);
        sidecar.Entries.Add(new() { File = fileName, Tag = tagName, Removed = removed, Ts = DateTime.UtcNow });
        WriteFileAndMarkMerged(folderPath, sidecar);
    }

    // 書き込み後に merged.ts を更新して、ウォッチャー起因の冗長 merge を防ぐ
    private void WriteFileAndMarkMerged(string folderPath, SidecarFile file)
    {
        WriteFile(folderPath, file);
        PersistMergedAt(folderPath, File.GetLastWriteTimeUtc(SidecarPath(folderPath)));
    }

    private static void RemoveExisting(SidecarFile sidecar, string fileName, string tagName)
        => sidecar.Entries.RemoveAll(e =>
            string.Equals(e.File, fileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Tag, tagName, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var w in _watchers.Values) { w.EnableRaisingEvents = false; w.Dispose(); }
            _watchers.Clear();
            _callbacks.Clear();
        }
    }
}
