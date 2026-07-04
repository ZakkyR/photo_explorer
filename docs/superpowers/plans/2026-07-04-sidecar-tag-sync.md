# サイドカーファイルによるタグ同期 実装プラン

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 各写真フォルダに `.photoexplorer/tags.json` を配置し、OneDrive 経由で複数 PC 間のタグを自動同期する。

**Architecture:** ローカル SQLite DB は高速クエリ用キャッシュとして維持し、タグの変更時に同フォルダ内の `tags.json` へも書き出す。フォルダを開く際に `tags.json` を読み込んで SQLite にマージする。`FileSystemWatcher` で `tags.json` の変更を検知し、別 PC の変更をリアルタイムに反映する。

**Tech Stack:** .NET 8, C#, WPF, SQLite + EF Core, System.Text.Json, xUnit

## Global Constraints

- ターゲットフレームワーク: net8.0-windows (App), net8.0 (Core/Data/Tests)
- テストフレームワーク: xUnit 2.9.3
- JSON: System.Text.Json（`JsonPropertyName` 属性でスネークケースキー）
- パスの比較は常に `StringComparison.OrdinalIgnoreCase`
- 既存 SQLite データを破壊しない（移行は追記のみ）
- `.photoexplorer` フォルダには `FileAttributes.Hidden` を設定する
- `tags.json` の書き出しは一時ファイル経由でアトミックに行う

---

## ファイル構成

### 新規作成
- `src/PhotoExplorer.Core/Models/SidecarEntry.cs` — JSON エントリモデル
- `src/PhotoExplorer.Core/Models/SidecarFile.cs` — JSON ファイルコンテナ + `GetLatestEntries()`
- `src/PhotoExplorer.Core/Services/ISidecarService.cs` — サービスインターフェース
- `src/PhotoExplorer.Core/Services/SidecarService.cs` — ファイル読み書き・マージ・FileSystemWatcher
- `tests/PhotoExplorer.Tests/Services/SidecarServiceTests.cs` — テスト

### 変更
- `src/PhotoExplorer.Core/Services/TagService.cs` — タグ変更時にサイドカー更新を追加
- `src/PhotoExplorer.Core/Services/ImageService.cs` — フォルダ読み込み前に `MergeIntoDbAsync` を呼ぶ
- `src/PhotoExplorer.App/ViewModels/MainViewModel.cs` — `ISidecarService` 追加、フォルダ切り替え時に watcher 開始・停止
- `src/PhotoExplorer.App/MainWindow.xaml.cs` — DI から `ISidecarService` を取得
- `src/PhotoExplorer.App/App.xaml.cs` — DI 登録 + 初回移行ロジック

---

## Task 1: SidecarEntry / SidecarFile モデル

**Files:**
- Create: `src/PhotoExplorer.Core/Models/SidecarEntry.cs`
- Create: `src/PhotoExplorer.Core/Models/SidecarFile.cs`
- Test: `tests/PhotoExplorer.Tests/Services/SidecarServiceTests.cs`（このタスク分のみ）

**Interfaces:**
- Produces:
  - `SidecarEntry` — `File`, `Tag`, `Removed`, `Ts` プロパティを持つクラス
  - `SidecarFile` — `Version`, `Entries` プロパティ + `GetLatestEntries(): List<SidecarEntry>`

- [ ] **Step 1: 失敗するテストを書く**

`tests/PhotoExplorer.Tests/Services/SidecarServiceTests.cs` を作成:

```csharp
using System.Text.Json;
using PhotoExplorer.Core.Models;

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
```

- [ ] **Step 2: テストが失敗することを確認**

```
dotnet test tests/PhotoExplorer.Tests --filter "FullyQualifiedName~SidecarModelTests" -v minimal
```
期待: FAIL（型が存在しない）

- [ ] **Step 3: SidecarEntry を作成**

`src/PhotoExplorer.Core/Models/SidecarEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace PhotoExplorer.Core.Models;

public class SidecarEntry
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";

    [JsonPropertyName("removed")]
    public bool Removed { get; set; }

    [JsonPropertyName("ts")]
    public DateTime Ts { get; set; }
}
```

- [ ] **Step 4: SidecarFile を作成**

`src/PhotoExplorer.Core/Models/SidecarFile.cs`:

```csharp
using System.Text.Json.Serialization;

namespace PhotoExplorer.Core.Models;

public class SidecarFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("entries")]
    public List<SidecarEntry> Entries { get; set; } = [];

    public List<SidecarEntry> GetLatestEntries()
        => Entries
            .GroupBy(e => (
                File: e.File.ToUpperInvariant(),
                Tag: e.Tag.ToUpperInvariant()))
            .Select(g => g.OrderByDescending(e => e.Ts).First())
            .ToList();
}
```

- [ ] **Step 5: テストが通ることを確認**

```
dotnet test tests/PhotoExplorer.Tests --filter "FullyQualifiedName~SidecarModelTests" -v minimal
```
期待: PASS (3 tests)

- [ ] **Step 6: コミット**

```bash
git add src/PhotoExplorer.Core/Models/SidecarEntry.cs \
        src/PhotoExplorer.Core/Models/SidecarFile.cs \
        tests/PhotoExplorer.Tests/Services/SidecarServiceTests.cs
git commit -m "feat: SidecarEntry / SidecarFile モデルを追加"
```

---

## Task 2: ISidecarService と SidecarService（ファイル読み書き・マージ）

**Files:**
- Create: `src/PhotoExplorer.Core/Services/ISidecarService.cs`
- Create: `src/PhotoExplorer.Core/Services/SidecarService.cs`
- Modify: `tests/PhotoExplorer.Tests/Services/SidecarServiceTests.cs`（テスト追加）

**Interfaces:**
- Consumes: `SidecarFile`, `SidecarEntry`（Task 1）, `AppDbContext`
- Produces:
  - `ISidecarService.MergeIntoDbAsync(string folderPath): Task`
  - `ISidecarService.AddEntryAsync(string filePath, string tagName): Task`
  - `ISidecarService.AddEntryBulkAsync(IReadOnlyList<string> filePaths, string tagName): Task`
  - `ISidecarService.RemoveEntryAsync(string filePath, string tagName): Task`
  - `ISidecarService.RemoveEntryBulkAsync(IReadOnlyList<string> filePaths, string tagName): Task`
  - `ISidecarService.WriteInitialTagsAsync(string folderPath, IReadOnlyList<(string fileName, string tagName)> tags): Task`
  - `ISidecarService.StartWatching(string folderPath, Func<string, Task> onChanged): void`（Task 3 で実装）
  - `ISidecarService.StopWatching(string folderPath): void`（Task 3 で実装）
  - `ISidecarService` は `IDisposable` を継承

- [ ] **Step 1: 失敗するテストを追加**

`tests/PhotoExplorer.Tests/Services/SidecarServiceTests.cs` の末尾に追加:

```csharp
public class SidecarServiceTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

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
```

- [ ] **Step 2: テストが失敗することを確認**

```
dotnet test tests/PhotoExplorer.Tests --filter "FullyQualifiedName~SidecarServiceTests" -v minimal
```
期待: FAIL（型が存在しない）

- [ ] **Step 3: ISidecarService を作成**

`src/PhotoExplorer.Core/Services/ISidecarService.cs`:

```csharp
namespace PhotoExplorer.Core.Services;

public interface ISidecarService : IDisposable
{
    Task MergeIntoDbAsync(string folderPath);
    Task AddEntryAsync(string filePath, string tagName);
    Task AddEntryBulkAsync(IReadOnlyList<string> filePaths, string tagName);
    Task RemoveEntryAsync(string filePath, string tagName);
    Task RemoveEntryBulkAsync(IReadOnlyList<string> filePaths, string tagName);
    Task WriteInitialTagsAsync(string folderPath, IReadOnlyList<(string fileName, string tagName)> tags);
    void StartWatching(string folderPath, Func<string, Task> onChanged);
    void StopWatching(string folderPath);
}
```

- [ ] **Step 4: SidecarService を作成**

`src/PhotoExplorer.Core/Services/SidecarService.cs`:

```csharp
using System.Text.Json;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Data;

namespace PhotoExplorer.Core.Services;

public class SidecarService : ISidecarService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private const string SidecarDirName = ".photoexplorer";
    private const string SidecarFileName = "tags.json";

    private readonly AppDbContext _ctx;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string, Task>> _callbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public SidecarService(AppDbContext ctx) => _ctx = ctx;

    // ── ファイルパスヘルパー ──────────────────────────────────────

    private static string SidecarDir(string folderPath)
        => Path.Combine(folderPath, SidecarDirName);

    private static string SidecarPath(string folderPath)
        => Path.Combine(folderPath, SidecarDirName, SidecarFileName);

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
        var sidecar = ReadFile(folderPath);
        var latest = sidecar.GetLatestEntries();
        if (latest.Count == 0) return;

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
                WriteFile(group.Key, sidecar);
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
                WriteFile(group.Key, sidecar);
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

    private static void UpdateSidecar(string folderPath, string fileName, string tagName, bool removed)
    {
        var sidecar = ReadFile(folderPath);
        RemoveExisting(sidecar, fileName, tagName);
        sidecar.Entries.Add(new() { File = fileName, Tag = tagName, Removed = removed, Ts = DateTime.UtcNow });
        WriteFile(folderPath, sidecar);
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
```

- [ ] **Step 5: テストが通ることを確認**

```
dotnet test tests/PhotoExplorer.Tests --filter "FullyQualifiedName~SidecarServiceTests" -v minimal
```
期待: PASS (6 tests)

- [ ] **Step 6: 全テストが通ることを確認**

```
dotnet test tests/PhotoExplorer.Tests -v minimal
```
期待: 全 PASS

- [ ] **Step 7: コミット**

```bash
git add src/PhotoExplorer.Core/Services/ISidecarService.cs \
        src/PhotoExplorer.Core/Services/SidecarService.cs \
        tests/PhotoExplorer.Tests/Services/SidecarServiceTests.cs
git commit -m "feat: ISidecarService と SidecarService を追加（読み書き・マージ・Watcher）"
```

---

## Task 3: TagService と ImageService の統合

**Files:**
- Modify: `src/PhotoExplorer.Core/Services/TagService.cs:17`
- Modify: `src/PhotoExplorer.Core/Services/ImageService.cs:9`
- Test: `tests/PhotoExplorer.Tests/Services/TagServiceTests.cs`（既存テストが通ることを確認）

**Interfaces:**
- Consumes: `ISidecarService`（Task 2）
- `TagService(AppDbContext ctx, ISidecarService? sidecar = null)` — sidecar は省略可（既存テスト互換）
- `ImageService(IFolderService folderService, ISidecarService sidecarService)`

- [ ] **Step 1: TagService に ISidecarService を追加**

`src/PhotoExplorer.Core/Services/TagService.cs` の先頭部分を変更:

変更前:
```csharp
public class TagService : ITagService
{
    // ...
    private readonly AppDbContext _ctx;

    public TagService(AppDbContext ctx) => _ctx = ctx;
```

変更後:
```csharp
public class TagService : ITagService
{
    // ...
    private readonly AppDbContext _ctx;
    private readonly ISidecarService? _sidecar;

    public TagService(AppDbContext ctx, ISidecarService? sidecar = null)
    {
        _ctx = ctx;
        _sidecar = sidecar;
    }
```

- [ ] **Step 2: AddTagAsync にサイドカー更新を追加**

`TagService.AddTagAsync` の末尾に追加（`ExecuteSqlInterpolatedAsync` の後）:

```csharp
public async Task AddTagAsync(string filePath, string tagName)
{
    await _ctx.Database.ExecuteSqlInterpolatedAsync(
        $"""
         INSERT INTO ImageTags (FilePath, TagName)
         SELECT {filePath}, {tagName}
         WHERE NOT EXISTS (
             SELECT 1 FROM ImageTags
             WHERE FilePath = {filePath} AND TagName = {tagName}
         )
         """);
    if (_sidecar != null) await _sidecar.AddEntryAsync(filePath, tagName);
}
```

- [ ] **Step 3: AddTagBulkAsync にサイドカー更新を追加**

`TagService.AddTagBulkAsync` の末尾に追加（`tx.CommitAsync()` の後）:

```csharp
public async Task AddTagBulkAsync(IReadOnlyList<string> filePaths, string tagName)
{
    if (filePaths.Count == 0) return;
    await using var tx = await _ctx.Database.BeginTransactionAsync();
    foreach (var fp in filePaths)
        await _ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO ImageTags (FilePath, TagName) SELECT {fp}, {tagName} WHERE NOT EXISTS (SELECT 1 FROM ImageTags WHERE FilePath = {fp} AND TagName = {tagName})");
    await tx.CommitAsync();
    if (_sidecar != null) await _sidecar.AddEntryBulkAsync(filePaths, tagName);
}
```

- [ ] **Step 4: RemoveTagAsync にサイドカー更新を追加**

```csharp
public async Task RemoveTagAsync(string filePath, string tagName)
{
    await _ctx.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM ImageTags WHERE FilePath = {filePath} AND TagName = {tagName}");
    if (_sidecar != null) await _sidecar.RemoveEntryAsync(filePath, tagName);
}
```

- [ ] **Step 5: RemoveTagBulkAsync にサイドカー更新を追加**

```csharp
public async Task RemoveTagBulkAsync(IReadOnlyList<string> filePaths, string tagName)
{
    if (filePaths.Count == 0) return;
    await using var tx = await _ctx.Database.BeginTransactionAsync();
    foreach (var fp in filePaths)
        await _ctx.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM ImageTags WHERE FilePath = {fp} AND TagName = {tagName}");
    await tx.CommitAsync();
    if (_sidecar != null) await _sidecar.RemoveEntryBulkAsync(filePaths, tagName);
}
```

- [ ] **Step 6: ImageService に ISidecarService を追加**

`src/PhotoExplorer.Core/Services/ImageService.cs` を全面置換:

```csharp
using PhotoExplorer.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PhotoExplorer.Core.Services;

public class ImageService : IImageService
{
    private readonly IFolderService _folderService;
    private readonly ISidecarService _sidecarService;

    public ImageService(IFolderService folderService, ISidecarService sidecarService)
    {
        _folderService = folderService;
        _sidecarService = sidecarService;
    }

    public async Task<IReadOnlyList<ImageItem>> LoadImagesFromFolderAsync(
        string folderPath, ITagService tagService)
    {
        await _sidecarService.MergeIntoDbAsync(folderPath);
        var files = _folderService.GetImageFilesInFolder(folderPath).ToList();
        var tagsBulk = await tagService.GetTagsBulkAsync(files);
        return files.Select(f => new ImageItem(f)
        {
            Tags = tagsBulk.TryGetValue(f, out var t) ? t : new List<Tag>()
        }).ToList();
    }

    public async Task<IReadOnlyList<ImageItem>> LoadImagesFromAlbumAsync(
        Album album, ITagService tagService)
    {
        var items = new List<ImageItem>();
        foreach (var path in album.FolderPaths)
            items.AddRange(await LoadImagesFromFolderAsync(path, tagService));
        return items;
    }

    public async Task<byte[]?> GenerateThumbnailAsync(string filePath, int maxSize = 200)
    {
        try
        {
            using var image = await Image.LoadAsync(filePath);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxSize, maxSize),
                Mode = ResizeMode.Max
            }));
            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
```

- [ ] **Step 7: 全テストが通ることを確認**

```
dotnet test tests/PhotoExplorer.Tests -v minimal
```
期待: 全 PASS（既存の TagServiceTests も含む）

- [ ] **Step 8: コミット**

```bash
git add src/PhotoExplorer.Core/Services/TagService.cs \
        src/PhotoExplorer.Core/Services/ImageService.cs
git commit -m "feat: TagService / ImageService にサイドカー統合を追加"
```

---

## Task 4: MainViewModel・MainWindow・App.xaml.cs の統合 + 移行

**Files:**
- Modify: `src/PhotoExplorer.App/ViewModels/MainViewModel.cs`
- Modify: `src/PhotoExplorer.App/MainWindow.xaml.cs`
- Modify: `src/PhotoExplorer.App/App.xaml.cs`

**Interfaces:**
- Consumes: `ISidecarService`（Task 2）, `SidecarService`（Task 2）

- [ ] **Step 1: MainViewModel に ISidecarService を追加**

`src/PhotoExplorer.App/ViewModels/MainViewModel.cs` のフィールド・コンストラクタを変更:

変更前（26〜58行目付近）:
```csharp
    private readonly IFolderService _folderService;
    private readonly IAlbumService _albumService;
    private readonly IImageService _imageService;
    private readonly ITagService _tagService;

    // ...

    public MainViewModel(
        IFolderService folderService,
        IAlbumService albumService,
        IImageService imageService,
        ITagService tagService)
    {
        _folderService = folderService;
        _albumService = albumService;
        _imageService = imageService;
        _tagService = tagService;

        _folderService.FolderChanged += OnFolderChanged;
    }
```

変更後:
```csharp
    private readonly IFolderService _folderService;
    private readonly IAlbumService _albumService;
    private readonly IImageService _imageService;
    private readonly ITagService _tagService;
    private readonly ISidecarService _sidecarService;

    // ...

    public MainViewModel(
        IFolderService folderService,
        IAlbumService albumService,
        IImageService imageService,
        ITagService tagService,
        ISidecarService sidecarService)
    {
        _folderService = folderService;
        _albumService = albumService;
        _imageService = imageService;
        _tagService = tagService;
        _sidecarService = sidecarService;

        _folderService.FolderChanged += OnFolderChanged;
    }
```

- [ ] **Step 2: SelectFolderAsync に watcher 開始・停止を追加**

`MainViewModel.SelectFolderAsync`（139〜146行目付近）を変更:

変更前:
```csharp
    public async Task SelectFolderAsync(string path)
    {
        SelectedFolder = path;
        SelectedAlbum = null;
        SelectedFolderPath = path;
        App.AppSettings.LastSelectedFolder = path;
        await LoadImagesAsync(await _imageService.LoadImagesFromFolderAsync(path, _tagService));
    }
```

変更後:
```csharp
    public async Task SelectFolderAsync(string path)
    {
        if (SelectedFolder != null) _sidecarService.StopWatching(SelectedFolder);

        SelectedFolder = path;
        SelectedAlbum = null;
        SelectedFolderPath = path;
        App.AppSettings.LastSelectedFolder = path;
        await LoadImagesAsync(await _imageService.LoadImagesFromFolderAsync(path, _tagService));

        _sidecarService.StartWatching(path, async _ =>
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await LoadImagesAsync(
                    await _imageService.LoadImagesFromFolderAsync(path, _tagService));
            });
        });
    }
```

- [ ] **Step 3: MainWindow.xaml.cs を更新**

`src/PhotoExplorer.App/MainWindow.xaml.cs` の `MainWindow()` を変更:

変更前:
```csharp
        ViewModel = new MainViewModel(
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IFolderService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IAlbumService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IImageService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.ITagService>());
```

変更後:
```csharp
        ViewModel = new MainViewModel(
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IFolderService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IAlbumService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IImageService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.ITagService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.ISidecarService>());
```

- [ ] **Step 4: App.xaml.cs を更新（DI 登録 + 移行）**

`src/PhotoExplorer.App/App.xaml.cs` の `OnStartup` を変更。`services.AddSingleton` ブロック（59〜65行目付近）を変更:

変更前:
```csharp
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IFolderService>(sp => new FolderService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<ITagService>(sp => new TagService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<IAlbumService>(sp => new AlbumService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<IImageService>(sp => new ImageService(sp.GetRequiredService<IFolderService>()));
        services.AddTransient<MainWindow>();
        Services = services.BuildServiceProvider();
```

変更後:
```csharp
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<ISidecarService>(sp => new SidecarService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<IFolderService>(sp => new FolderService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<ITagService>(sp => new TagService(
            sp.GetRequiredService<AppDbContext>(),
            sp.GetRequiredService<ISidecarService>()));
        services.AddSingleton<IAlbumService>(sp => new AlbumService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<IImageService>(sp => new ImageService(
            sp.GetRequiredService<IFolderService>(),
            sp.GetRequiredService<ISidecarService>()));
        services.AddTransient<MainWindow>();
        Services = services.BuildServiceProvider();
```

- [ ] **Step 5: 既存データの移行を App.xaml.cs に追加**

`Services = services.BuildServiceProvider();` の直後に追加:

```csharp
        // 既存 SQLite タグを tags.json に移行（初回のみ）
        var migrationFlag = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhotoExplorer", "migration_v1.done");
        if (!File.Exists(migrationFlag))
        {
            var sidecarSvc = Services.GetRequiredService<ISidecarService>();
            var existingTags = dbContext.ImageTags
                .Select(t => new { t.FilePath, t.TagName })
                .ToList();
            var groups = existingTags
                .Where(t => !string.IsNullOrEmpty(Path.GetDirectoryName(t.FilePath)))
                .GroupBy(
                    t => Path.GetDirectoryName(t.FilePath)!,
                    StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                if (!Directory.Exists(group.Key)) continue;
                sidecarSvc.WriteInitialTagsAsync(
                    group.Key,
                    group.Select(t => (Path.GetFileName(t.FilePath), t.TagName))
                         .ToList())
                    .GetAwaiter().GetResult();
            }
            File.WriteAllText(migrationFlag, DateTime.UtcNow.ToString("O"));
        }
```

- [ ] **Step 6: using を追加**（App.xaml.cs の先頭に）

```csharp
using PhotoExplorer.Core.Services;
```

- [ ] **Step 7: ビルドが通ることを確認**

```
dotnet build src/PhotoExplorer.App/PhotoExplorer.App.csproj -v minimal
```
期待: ビルドエラーなし

- [ ] **Step 8: アプリを起動して手動確認**

1. アプリを起動する
2. OneDrive フォルダを選択する
3. 写真にタグを付ける
4. フォルダ内の `.photoexplorer/tags.json` が作成されていることを確認
5. `tags.json` の内容を確認（追加したタグが `removed: false` で記録されている）
6. タグを削除し、`tags.json` に `removed: true` のエントリが追加されることを確認
7. ローカルフォルダを選択した場合も同様に `tags.json` が作成されることを確認

- [ ] **Step 9: 全テストが通ることを確認**

```
dotnet test tests/PhotoExplorer.Tests -v minimal
```
期待: 全 PASS

- [ ] **Step 10: コミット**

```bash
git add src/PhotoExplorer.App/ViewModels/MainViewModel.cs \
        src/PhotoExplorer.App/MainWindow.xaml.cs \
        src/PhotoExplorer.App/App.xaml.cs
git commit -m "feat: MainViewModel / App.xaml.cs にサイドカー統合と既存データ移行を追加"
```

---

## セルフレビュー

スペック要件との対照:

| 要件 | 対応タスク |
|------|-----------|
| `.photoexplorer/tags.json` サイドカーファイル | Task 1, 2 |
| 隠しフォルダ属性 | Task 2 `WriteFile()` |
| タイムスタンプ付きエントリ（追加・削除） | Task 1 モデル |
| 最新 ts で競合解決 | Task 1 `GetLatestEntries()` |
| フォルダ開放時にマージ | Task 3 `ImageService` |
| タグ変更時に JSON 更新 | Task 3 `TagService` |
| `FileSystemWatcher` でリアルタイム検知 | Task 2 `SidecarService` |
| OneDrive 競合ファイルのマージ | Task 2 `HandleConflictFileAsync` |
| 既存 SQLite データの移行 | Task 4 `App.xaml.cs` |
| ローカルフォルダでも正常動作 | Task 2（特別判定なし） |
