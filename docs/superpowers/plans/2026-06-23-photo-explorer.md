# Photo Explorer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タグ付け・アルバム・D&D・フローティングプレビューを備えた Windows 向け画像整理 WPF アプリを構築する。

**Architecture:** 3 プロジェクト構成（App / Core / Data）の WPF アプリ。CommunityToolkit.Mvvm で MVVM を実装し、Microsoft.Extensions.DependencyInjection で DI を行う。SQLite + EF Core でデータをローカル DB 管理し、JPEG/PNG のタグは IPTC キーワードとして画像ファイルに書き込み、RAW 等は SQLite にフォールバック保存する。

**Tech Stack:** WPF (.NET 8), EF Core 8 + SQLite, SixLabors.ImageSharp 3.x, CommunityToolkit.Mvvm 8.x, Microsoft.Extensions.DependencyInjection 8.x, xUnit 2.x

## Global Constraints
- ターゲットフレームワーク: `net8.0-windows` (UseWPF=true)
- DB 保存先: `%APPDATA%\PhotoExplorer\photo_explorer.db`
- IPTC キーワード書込対象: `.jpg`, `.jpeg`, `.png` のみ（大文字小文字無視）
- タグ複数選択時の絞込は OR 条件（いずれかのタグを持つ画像を表示）
- 想定最大枚数: 1 フォルダあたり 200 枚（グリッドは仮想化不要）
- D&D は `DataFormats.FileDrop` 形式（Windows Explorer 互換）
- ウィンドウ状態は `%APPDATA%\PhotoExplorer\settings.json` に JSON 保存
- MVVM: CommunityToolkit.Mvvm の `[ObservableProperty]` / `[RelayCommand]` を使用
- 対応拡張子: `.jpg`, `.jpeg`, `.png`, `.webp`, `.bmp`, `.tiff`, `.tif`, `.raw`, `.cr2`, `.nef`, `.arw`, `.dng`

---

## File Structure

```
photo_explorer/
├── PhotoExplorer.sln
├── src/
│   ├── PhotoExplorer.App/              ← WPF アプリ (net8.0-windows)
│   │   ├── PhotoExplorer.App.csproj
│   │   ├── App.xaml + App.xaml.cs      ← DI ブートストラップ
│   │   ├── AppSettings.cs              ← ウィンドウ状態 JSON 管理
│   │   ├── MainWindow.xaml + .cs       ← メインウィンドウ
│   │   ├── PreviewWindow.xaml + .cs    ← フローティングプレビュー
│   │   ├── ViewModels/
│   │   │   ├── MainViewModel.cs        ← フォルダ/アルバム選択・タグフィルタ
│   │   │   ├── ImageItemViewModel.cs   ← 画像1枚分の表示用 VM
│   │   │   └── PreviewViewModel.cs     ← プレビュー・左右ナビ
│   │   └── Views/
│   │       ├── SidebarView.xaml + .cs  ← フォルダ/アルバムツリー
│   │       ├── ImageGridView.xaml + .cs← サムネイルグリッド + D&D
│   │       └── TagFilterView.xaml + .cs← タグ絞込バー
│   ├── PhotoExplorer.Core/             ← ドメインロジック (net8.0)
│   │   ├── PhotoExplorer.Core.csproj
│   │   ├── Models/
│   │   │   ├── ImageItem.cs
│   │   │   ├── Tag.cs
│   │   │   └── Album.cs
│   │   └── Services/
│   │       ├── IFolderService.cs + FolderService.cs
│   │       ├── IImageService.cs + ImageService.cs
│   │       ├── ITagService.cs + TagService.cs
│   │       └── IAlbumService.cs + AlbumService.cs
│   └── PhotoExplorer.Data/             ← EF Core + SQLite (net8.0)
│       ├── PhotoExplorer.Data.csproj
│       ├── AppDbContext.cs
│       └── Entities/
│           ├── FolderEntity.cs
│           ├── AlbumEntity.cs
│           ├── AlbumFolderEntity.cs
│           └── ImageTagEntity.cs
└── tests/
    └── PhotoExplorer.Tests/
        ├── PhotoExplorer.Tests.csproj
        ├── Data/
        │   └── AppDbContextTests.cs
        └── Services/
            ├── FolderServiceTests.cs
            ├── TagServiceTests.cs
            └── AlbumServiceTests.cs
```

---

### Task 1: Solution Scaffold

**Files:**
- Create: `PhotoExplorer.sln`
- Create: `src/PhotoExplorer.App/PhotoExplorer.App.csproj`
- Create: `src/PhotoExplorer.Core/PhotoExplorer.Core.csproj`
- Create: `src/PhotoExplorer.Data/PhotoExplorer.Data.csproj`
- Create: `tests/PhotoExplorer.Tests/PhotoExplorer.Tests.csproj`

**Interfaces:**
- Produces: ビルド可能な 4 プロジェクト構成のソリューション

- [ ] **Step 1: ソリューションと各プロジェクトを作成する**

```powershell
cd C:\Users\Zakky\source\repos\photo_explorer

dotnet new sln -n PhotoExplorer
dotnet new wpf -n PhotoExplorer.App -o src/PhotoExplorer.App --framework net8.0-windows
dotnet new classlib -n PhotoExplorer.Core -o src/PhotoExplorer.Core --framework net8.0
dotnet new classlib -n PhotoExplorer.Data -o src/PhotoExplorer.Data --framework net8.0
dotnet new xunit -n PhotoExplorer.Tests -o tests/PhotoExplorer.Tests --framework net8.0

dotnet sln add src/PhotoExplorer.App/PhotoExplorer.App.csproj
dotnet sln add src/PhotoExplorer.Core/PhotoExplorer.Core.csproj
dotnet sln add src/PhotoExplorer.Data/PhotoExplorer.Data.csproj
dotnet sln add tests/PhotoExplorer.Tests/PhotoExplorer.Tests.csproj
```

- [ ] **Step 2: プロジェクト参照を追加する**

```powershell
dotnet add src/PhotoExplorer.App/PhotoExplorer.App.csproj reference src/PhotoExplorer.Core/PhotoExplorer.Core.csproj
dotnet add src/PhotoExplorer.App/PhotoExplorer.App.csproj reference src/PhotoExplorer.Data/PhotoExplorer.Data.csproj
dotnet add src/PhotoExplorer.Core/PhotoExplorer.Core.csproj reference src/PhotoExplorer.Data/PhotoExplorer.Data.csproj
dotnet add tests/PhotoExplorer.Tests/PhotoExplorer.Tests.csproj reference src/PhotoExplorer.Core/PhotoExplorer.Core.csproj
dotnet add tests/PhotoExplorer.Tests/PhotoExplorer.Tests.csproj reference src/PhotoExplorer.Data/PhotoExplorer.Data.csproj
```

- [ ] **Step 3: NuGet パッケージを追加する**

```powershell
# PhotoExplorer.Data
dotnet add src/PhotoExplorer.Data/PhotoExplorer.Data.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.*
dotnet add src/PhotoExplorer.Data/PhotoExplorer.Data.csproj package Microsoft.EntityFrameworkCore.Design --version 8.0.*

# PhotoExplorer.Core
dotnet add src/PhotoExplorer.Core/PhotoExplorer.Core.csproj package SixLabors.ImageSharp --version 3.1.*
dotnet add src/PhotoExplorer.Core/PhotoExplorer.Core.csproj package Microsoft.EntityFrameworkCore --version 8.0.*

# PhotoExplorer.App
dotnet add src/PhotoExplorer.App/PhotoExplorer.App.csproj package CommunityToolkit.Mvvm --version 8.3.*
dotnet add src/PhotoExplorer.App/PhotoExplorer.App.csproj package Microsoft.Extensions.DependencyInjection --version 8.0.*
dotnet add src/PhotoExplorer.App/PhotoExplorer.App.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.*

# PhotoExplorer.Tests
dotnet add tests/PhotoExplorer.Tests/PhotoExplorer.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory --version 8.0.*
```

- [ ] **Step 4: PhotoExplorer.Core.csproj と PhotoExplorer.Data.csproj を確認する**

`src/PhotoExplorer.Core/PhotoExplorer.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`src/PhotoExplorer.Data/PhotoExplorer.Data.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: デフォルトの Class1.cs を削除する**

```powershell
Remove-Item src/PhotoExplorer.Core/Class1.cs -ErrorAction SilentlyContinue
Remove-Item src/PhotoExplorer.Data/Class1.cs -ErrorAction SilentlyContinue
```

- [ ] **Step 6: ビルドを確認する**

```powershell
dotnet build
```
Expected: `Build succeeded`

- [ ] **Step 7: git 初期化してコミットする**

```powershell
git init
git add .
git commit -m "chore: scaffold solution with App/Core/Data/Tests projects"
```

---

### Task 2: Data Layer (EF Core エンティティ + AppDbContext)

**Files:**
- Create: `src/PhotoExplorer.Data/Entities/FolderEntity.cs`
- Create: `src/PhotoExplorer.Data/Entities/AlbumEntity.cs`
- Create: `src/PhotoExplorer.Data/Entities/AlbumFolderEntity.cs`
- Create: `src/PhotoExplorer.Data/Entities/ImageTagEntity.cs`
- Create: `src/PhotoExplorer.Data/AppDbContext.cs`
- Create: `tests/PhotoExplorer.Tests/Data/AppDbContextTests.cs`

**Interfaces:**
- Produces: `AppDbContext` — 後続タスクのサービスが DI 経由で取得する EF Core コンテキスト

- [ ] **Step 1: テストを書く**

`tests/PhotoExplorer.Tests/Data/AppDbContextTests.cs`:
```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

```powershell
dotnet test tests/PhotoExplorer.Tests --filter "AppDbContextTests"
```
Expected: コンパイルエラーまたは FAIL

- [ ] **Step 3: エンティティを実装する**

`src/PhotoExplorer.Data/Entities/FolderEntity.cs`:
```csharp
namespace PhotoExplorer.Data.Entities;

public class FolderEntity
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
```

`src/PhotoExplorer.Data/Entities/AlbumEntity.cs`:
```csharp
namespace PhotoExplorer.Data.Entities;

public class AlbumEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<AlbumFolderEntity> AlbumFolders { get; set; } = new();
}
```

`src/PhotoExplorer.Data/Entities/AlbumFolderEntity.cs`:
```csharp
namespace PhotoExplorer.Data.Entities;

public class AlbumFolderEntity
{
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public AlbumEntity Album { get; set; } = null!;
    public string FolderPath { get; set; } = string.Empty;
}
```

`src/PhotoExplorer.Data/Entities/ImageTagEntity.cs`:
```csharp
namespace PhotoExplorer.Data.Entities;

public class ImageTagEntity
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
}
```

- [ ] **Step 4: AppDbContext を実装する**

`src/PhotoExplorer.Data/AppDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Data.Entities;

namespace PhotoExplorer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<FolderEntity> Folders => Set<FolderEntity>();
    public DbSet<AlbumEntity> Albums => Set<AlbumEntity>();
    public DbSet<AlbumFolderEntity> AlbumFolders => Set<AlbumFolderEntity>();
    public DbSet<ImageTagEntity> ImageTags => Set<ImageTagEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AlbumFolderEntity>()
            .HasOne(af => af.Album)
            .WithMany(a => a.AlbumFolders)
            .HasForeignKey(af => af.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ImageTagEntity>()
            .HasIndex(t => t.FilePath);
    }
}
```

- [ ] **Step 5: テストを実行して確認する**

```powershell
dotnet test tests/PhotoExplorer.Tests --filter "AppDbContextTests"
```
Expected: PASS (3 tests)

- [ ] **Step 6: コミットする**

```powershell
git add src/PhotoExplorer.Data/ tests/PhotoExplorer.Tests/Data/
git commit -m "feat: add data layer entities and AppDbContext"
```

---

### Task 3: Core Domain Models

**Files:**
- Create: `src/PhotoExplorer.Core/Models/ImageItem.cs`
- Create: `src/PhotoExplorer.Core/Models/Tag.cs`
- Create: `src/PhotoExplorer.Core/Models/Album.cs`

**Interfaces:**
- Produces:
  - `ImageItem(string filePath)` — `FilePath`, `FileName`, `Extension`, `Tags`, `ThumbnailBytes`
  - `Tag(string name)` — `Name` (値オブジェクト、等値比較あり)
  - `Album` — `Id`, `Name`, `FolderPaths`

- [ ] **Step 1: モデルを実装する**

`src/PhotoExplorer.Core/Models/Tag.cs`:
```csharp
namespace PhotoExplorer.Core.Models;

public class Tag
{
    public string Name { get; }
    public Tag(string name) => Name = name.Trim();
    public override string ToString() => Name;
    public override bool Equals(object? obj) => obj is Tag t && t.Name == Name;
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
}
```

`src/PhotoExplorer.Core/Models/ImageItem.cs`:
```csharp
namespace PhotoExplorer.Core.Models;

public class ImageItem
{
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
    public List<Tag> Tags { get; set; } = new();
    public byte[]? ThumbnailBytes { get; set; }

    public ImageItem(string filePath) => FilePath = filePath;
}
```

`src/PhotoExplorer.Core/Models/Album.cs`:
```csharp
namespace PhotoExplorer.Core.Models;

public class Album
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> FolderPaths { get; set; } = new();
}
```

- [ ] **Step 2: ビルドを確認する**

```powershell
dotnet build src/PhotoExplorer.Core/
```
Expected: `Build succeeded`

- [ ] **Step 3: コミットする**

```powershell
git add src/PhotoExplorer.Core/Models/
git commit -m "feat: add core domain models (ImageItem, Tag, Album)"
```

---

### Task 4: FolderService

**Files:**
- Create: `src/PhotoExplorer.Core/Services/IFolderService.cs`
- Create: `src/PhotoExplorer.Core/Services/FolderService.cs`
- Create: `tests/PhotoExplorer.Tests/Services/FolderServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`
- Produces:
  - `IFolderService.RegisterFolderAsync(string path) → Task`
  - `IFolderService.UnregisterFolderAsync(string path) → Task`
  - `IFolderService.GetRegisteredFoldersAsync() → Task<IReadOnlyList<string>>`
  - `IFolderService.GetImageFilesInFolder(string folderPath) → IEnumerable<string>`
  - `event EventHandler<FolderChangedEventArgs>? FolderChanged`

- [ ] **Step 1: テストを書く**

`tests/PhotoExplorer.Tests/Services/FolderServiceTests.cs`:
```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

```powershell
dotnet test tests/PhotoExplorer.Tests --filter "FolderServiceTests"
```
Expected: コンパイルエラーまたは FAIL

- [ ] **Step 3: IFolderService を実装する**

`src/PhotoExplorer.Core/Services/IFolderService.cs`:
```csharp
namespace PhotoExplorer.Core.Services;

public class FolderChangedEventArgs : EventArgs
{
    public string FolderPath { get; }
    public string FilePath { get; }
    public WatcherChangeTypes ChangeType { get; }

    public FolderChangedEventArgs(string folderPath, string filePath, WatcherChangeTypes changeType)
    {
        FolderPath = folderPath;
        FilePath = filePath;
        ChangeType = changeType;
    }
}

public interface IFolderService : IDisposable
{
    Task RegisterFolderAsync(string path);
    Task UnregisterFolderAsync(string path);
    Task<IReadOnlyList<string>> GetRegisteredFoldersAsync();
    IEnumerable<string> GetImageFilesInFolder(string folderPath);
    event EventHandler<FolderChangedEventArgs>? FolderChanged;
}
```

- [ ] **Step 4: FolderService を実装する**

`src/PhotoExplorer.Core/Services/FolderService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
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

    public async Task<IReadOnlyList<string>> GetRegisteredFoldersAsync()
        => await _ctx.Folders.Select(f => f.Path).ToListAsync();

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
```

- [ ] **Step 5: テストを実行する**

```powershell
dotnet test tests/PhotoExplorer.Tests --filter "FolderServiceTests"
```
Expected: PASS (4 tests)

- [ ] **Step 6: コミットする**

```powershell
git add src/PhotoExplorer.Core/Services/IFolderService.cs src/PhotoExplorer.Core/Services/FolderService.cs tests/PhotoExplorer.Tests/Services/FolderServiceTests.cs
git commit -m "feat: add FolderService with FileSystemWatcher support"
```

---

### Task 5: TagService

**Files:**
- Create: `src/PhotoExplorer.Core/Services/ITagService.cs`
- Create: `src/PhotoExplorer.Core/Services/TagService.cs`
- Create: `tests/PhotoExplorer.Tests/Services/TagServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`
- Produces:
  - `ITagService.GetTagsAsync(string filePath) → Task<IReadOnlyList<Tag>>`
  - `ITagService.AddTagAsync(string filePath, string tagName) → Task`
  - `ITagService.RemoveTagAsync(string filePath, string tagName) → Task`
  - `ITagService.GetAllTagNamesAsync() → Task<IReadOnlyList<string>>`

**Note:** JPEG/PNG は IPTC キーワードに書き込む。失敗または他拡張子は SQLite にフォールバック。

- [ ] **Step 1: テストを書く（DB パスのみ。実ファイルへの IPTC 書込はテスト対象外）**

`tests/PhotoExplorer.Tests/Services/TagServiceTests.cs`:
```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

```powershell
dotnet test tests/PhotoExplorer.Tests --filter "TagServiceTests"
```
Expected: コンパイルエラーまたは FAIL

- [ ] **Step 3: ITagService を実装する**

`src/PhotoExplorer.Core/Services/ITagService.cs`:
```csharp
using PhotoExplorer.Core.Models;

namespace PhotoExplorer.Core.Services;

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetTagsAsync(string filePath);
    Task AddTagAsync(string filePath, string tagName);
    Task RemoveTagAsync(string filePath, string tagName);
    Task<IReadOnlyList<string>> GetAllTagNamesAsync();
}
```

- [ ] **Step 4: TagService を実装する**

`src/PhotoExplorer.Core/Services/TagService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Data;
using PhotoExplorer.Data.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;

namespace PhotoExplorer.Core.Services;

public class TagService : ITagService
{
    private static readonly HashSet<string> IptcSupported =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    private readonly AppDbContext _ctx;

    public TagService(AppDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<Tag>> GetTagsAsync(string filePath)
    {
        if (IsIptcSupported(filePath))
        {
            var iptcTags = await ReadIptcKeywordsAsync(filePath);
            if (iptcTags.Count > 0) return iptcTags;
        }
        return await _ctx.ImageTags
            .Where(t => t.FilePath == filePath)
            .Select(t => new Tag(t.TagName))
            .ToListAsync();
    }

    public async Task AddTagAsync(string filePath, string tagName)
    {
        if (IsIptcSupported(filePath) && await WriteIptcKeywordAsync(filePath, tagName, add: true))
            return;

        if (!await _ctx.ImageTags.AnyAsync(t => t.FilePath == filePath && t.TagName == tagName))
        {
            _ctx.ImageTags.Add(new ImageTagEntity { FilePath = filePath, TagName = tagName });
            await _ctx.SaveChangesAsync();
        }
    }

    public async Task RemoveTagAsync(string filePath, string tagName)
    {
        if (IsIptcSupported(filePath) && await WriteIptcKeywordAsync(filePath, tagName, add: false))
            return;

        var entity = await _ctx.ImageTags
            .FirstOrDefaultAsync(t => t.FilePath == filePath && t.TagName == tagName);
        if (entity != null)
        {
            _ctx.ImageTags.Remove(entity);
            await _ctx.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<string>> GetAllTagNamesAsync()
        => await _ctx.ImageTags.Select(t => t.TagName).Distinct().ToListAsync();

    private static bool IsIptcSupported(string filePath) =>
        IptcSupported.Contains(Path.GetExtension(filePath));

    private static async Task<IReadOnlyList<Tag>> ReadIptcKeywordsAsync(string filePath)
    {
        try
        {
            using var image = await Image.LoadAsync(filePath);
            var iptc = image.Metadata.IptcProfile;
            if (iptc == null) return Array.Empty<Tag>();
            return iptc.GetValues(IptcTag.Keyword)
                .Select(v => new Tag(v.Value.ToString()))
                .ToList();
        }
        catch { return Array.Empty<Tag>(); }
    }

    private static async Task<bool> WriteIptcKeywordAsync(string filePath, string tagName, bool add)
    {
        try
        {
            using var image = await Image.LoadAsync(filePath);
            var iptc = image.Metadata.IptcProfile ?? new IptcProfile();
            var existing = iptc.GetValues(IptcTag.Keyword)
                .Select(v => v.Value.ToString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (add) existing.Add(tagName);
            else existing.Remove(tagName);

            iptc.RemoveAllValues(IptcTag.Keyword);
            foreach (var kw in existing) iptc.SetValue(IptcTag.Keyword, kw);
            image.Metadata.IptcProfile = iptc;
            await image.SaveAsync(filePath);
            return true;
        }
        catch { return false; }
    }
}
```

- [ ] **Step 5: テストを実行する**

```powershell
dotnet test tests/PhotoExplorer.Tests --filter "TagServiceTests"
```
Expected: PASS (4 tests)

- [ ] **Step 6: コミットする**

```powershell
git add src/PhotoExplorer.Core/Services/ITagService.cs src/PhotoExplorer.Core/Services/TagService.cs tests/PhotoExplorer.Tests/Services/TagServiceTests.cs
git commit -m "feat: add TagService with IPTC write and SQLite fallback"
```

---

### Task 6: ImageService と AlbumService

**Files:**
- Create: `src/PhotoExplorer.Core/Services/IImageService.cs`
- Create: `src/PhotoExplorer.Core/Services/ImageService.cs`
- Create: `src/PhotoExplorer.Core/Services/IAlbumService.cs`
- Create: `src/PhotoExplorer.Core/Services/AlbumService.cs`
- Create: `tests/PhotoExplorer.Tests/Services/AlbumServiceTests.cs`

**Interfaces:**
- Consumes: `IFolderService`, `ITagService`, `AppDbContext`
- Produces:
  - `IImageService.LoadImagesFromFolderAsync(string folderPath, ITagService tagService) → Task<IReadOnlyList<ImageItem>>`
  - `IImageService.LoadImagesFromAlbumAsync(Album album, ITagService tagService) → Task<IReadOnlyList<ImageItem>>`
  - `IImageService.GenerateThumbnailAsync(string filePath, int maxSize = 200) → Task<byte[]?>`
  - `IAlbumService.CreateAlbumAsync(string name) → Task<Album>`
  - `IAlbumService.DeleteAlbumAsync(int albumId) → Task`
  - `IAlbumService.GetAlbumsAsync() → Task<IReadOnlyList<Album>>`
  - `IAlbumService.AddFolderToAlbumAsync(int albumId, string folderPath) → Task`
  - `IAlbumService.RemoveFolderFromAlbumAsync(int albumId, string folderPath) → Task`

- [ ] **Step 1: AlbumService のテストを書く**

`tests/PhotoExplorer.Tests/Services/AlbumServiceTests.cs`:
```csharp
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
```

- [ ] **Step 2: テストが失敗することを確認する**

```powershell
dotnet test tests/PhotoExplorer.Tests --filter "AlbumServiceTests"
```
Expected: FAIL

- [ ] **Step 3: IImageService を実装する**

`src/PhotoExplorer.Core/Services/IImageService.cs`:
```csharp
using PhotoExplorer.Core.Models;

namespace PhotoExplorer.Core.Services;

public interface IImageService
{
    Task<IReadOnlyList<ImageItem>> LoadImagesFromFolderAsync(string folderPath, ITagService tagService);
    Task<IReadOnlyList<ImageItem>> LoadImagesFromAlbumAsync(Album album, ITagService tagService);
    Task<byte[]?> GenerateThumbnailAsync(string filePath, int maxSize = 200);
}
```

`src/PhotoExplorer.Core/Services/ImageService.cs`:
```csharp
using PhotoExplorer.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PhotoExplorer.Core.Services;

public class ImageService : IImageService
{
    private readonly IFolderService _folderService;

    public ImageService(IFolderService folderService) => _folderService = folderService;

    public async Task<IReadOnlyList<ImageItem>> LoadImagesFromFolderAsync(
        string folderPath, ITagService tagService)
    {
        var files = _folderService.GetImageFilesInFolder(folderPath);
        var items = new List<ImageItem>();
        foreach (var file in files)
        {
            var item = new ImageItem(file)
            {
                Tags = (await tagService.GetTagsAsync(file)).ToList()
            };
            items.Add(item);
        }
        return items;
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

- [ ] **Step 4: IAlbumService と AlbumService を実装する**

`src/PhotoExplorer.Core/Services/IAlbumService.cs`:
```csharp
using PhotoExplorer.Core.Models;

namespace PhotoExplorer.Core.Services;

public interface IAlbumService
{
    Task<Album> CreateAlbumAsync(string name);
    Task DeleteAlbumAsync(int albumId);
    Task<IReadOnlyList<Album>> GetAlbumsAsync();
    Task AddFolderToAlbumAsync(int albumId, string folderPath);
    Task RemoveFolderFromAlbumAsync(int albumId, string folderPath);
}
```

`src/PhotoExplorer.Core/Services/AlbumService.cs`:
```csharp
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
```

- [ ] **Step 5: テストを実行する**

```powershell
dotnet test tests/PhotoExplorer.Tests --filter "AlbumServiceTests"
```
Expected: PASS (3 tests)

- [ ] **Step 6: コミットする**

```powershell
git add src/PhotoExplorer.Core/Services/ tests/PhotoExplorer.Tests/Services/AlbumServiceTests.cs
git commit -m "feat: add ImageService and AlbumService"
```

---

### Task 7: App Bootstrap (DI + 起動)

**Files:**
- Create: `src/PhotoExplorer.App/AppSettings.cs`
- Modify: `src/PhotoExplorer.App/App.xaml`
- Modify: `src/PhotoExplorer.App/App.xaml.cs`

**Interfaces:**
- Produces: DI コンテナ経由で全サービスが取得可能な状態。`AppSettings` による設定の読み書き。

- [ ] **Step 1: App.xaml から StartupUri を削除する**

`src/PhotoExplorer.App/App.xaml` を以下に書き換える:
```xml
<Application x:Class="PhotoExplorer.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: AppSettings を実装する**

`src/PhotoExplorer.App/AppSettings.cs`:
```csharp
using System.Text.Json;

namespace PhotoExplorer.App;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PhotoExplorer", "settings.json");

    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public string? LastSelectedFolder { get; set; }
    public double ThumbnailSize { get; set; } = 150;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }
}
```

- [ ] **Step 3: App.xaml.cs を実装する**

`src/PhotoExplorer.App/App.xaml.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoExplorer.Core.Services;
using PhotoExplorer.Data;
using System.Windows;

namespace PhotoExplorer.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static AppSettings AppSettings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppSettings = AppSettings.Load();

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhotoExplorer", "photo_explorer.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var dbContext = new AppDbContext(dbOptions);
        dbContext.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IFolderService>(sp => new FolderService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<ITagService>(sp => new TagService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<IAlbumService>(sp => new AlbumService(sp.GetRequiredService<AppDbContext>()));
        services.AddSingleton<IImageService>(sp => new ImageService(sp.GetRequiredService<IFolderService>()));
        services.AddTransient<MainWindow>();
        Services = services.BuildServiceProvider();

        Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppSettings.Save();
        (Services.GetService<IFolderService>() as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: ビルドを確認する**

```powershell
dotnet build src/PhotoExplorer.App/
```
Expected: `Build succeeded`

- [ ] **Step 5: コミットする**

```powershell
git add src/PhotoExplorer.App/AppSettings.cs src/PhotoExplorer.App/App.xaml src/PhotoExplorer.App/App.xaml.cs
git commit -m "feat: add DI bootstrap and AppSettings"
```

---

### Task 8: MainViewModel + MainWindow レイアウト + SidebarView

**Files:**
- Create: `src/PhotoExplorer.App/ViewModels/MainViewModel.cs`
- Create: `src/PhotoExplorer.App/ViewModels/ImageItemViewModel.cs`
- Modify: `src/PhotoExplorer.App/MainWindow.xaml`
- Modify: `src/PhotoExplorer.App/MainWindow.xaml.cs`
- Create: `src/PhotoExplorer.App/Views/SidebarView.xaml`
- Create: `src/PhotoExplorer.App/Views/SidebarView.xaml.cs`

**Interfaces:**
- Consumes: `IFolderService`, `IAlbumService`, `IImageService`, `ITagService`
- Produces:
  - `MainViewModel.Folders` — `ObservableCollection<string>`
  - `MainViewModel.Albums` — `ObservableCollection<Album>`
  - `MainViewModel.AllImages` — `ObservableCollection<ImageItemViewModel>`
  - `MainViewModel.FilteredImages` — `ObservableCollection<ImageItemViewModel>`（タグフィルタ後）
  - `MainViewModel.ThumbnailSize` — `double`（スライダーにバインド）
  - `MainViewModel.TagFilters` — `ObservableCollection<TagFilterItem>`
  - `MainViewModel.LoadFolderCommand(string path) → Task`
  - `MainViewModel.AddFolderCommand → IRelayCommand`
  - `MainViewModel.ApplyTagFilter()` — FilteredImages を更新

- [ ] **Step 1: ImageItemViewModel を実装する**

`src/PhotoExplorer.App/ViewModels/ImageItemViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoExplorer.Core.Models;
using System.IO;
using System.Windows.Media.Imaging;

namespace PhotoExplorer.App.ViewModels;

public partial class ImageItemViewModel : ObservableObject
{
    public ImageItem Model { get; }

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    public ImageItemViewModel(ImageItem model) => Model = model;

    public void SetThumbnailFromBytes(byte[]? bytes)
    {
        if (bytes == null) { Thumbnail = null; return; }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        Thumbnail = bitmap;
    }
}
```

- [ ] **Step 2: TagFilterItem を実装する**

`src/PhotoExplorer.App/ViewModels/TagFilterItem.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoExplorer.App.ViewModels;

public partial class TagFilterItem : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    public TagFilterItem(string name) => Name = name;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? SelectionChanged;
}
```

- [ ] **Step 3: MainViewModel を実装する**

`src/PhotoExplorer.App/ViewModels/MainViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace PhotoExplorer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFolderService _folderService;
    private readonly IAlbumService _albumService;
    private readonly IImageService _imageService;
    private readonly ITagService _tagService;

    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<Album> Albums { get; } = new();
    public ObservableCollection<ImageItemViewModel> AllImages { get; } = new();
    public ObservableCollection<ImageItemViewModel> FilteredImages { get; } = new();
    public ObservableCollection<TagFilterItem> TagFilters { get; } = new();

    [ObservableProperty]
    private double _thumbnailSize = App.AppSettings.ThumbnailSize;

    [ObservableProperty]
    private string? _selectedFolder;

    [ObservableProperty]
    private Album? _selectedAlbum;

    [ObservableProperty]
    private bool _isLoading;

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

    public async Task InitializeAsync()
    {
        var folders = await _folderService.GetRegisteredFoldersAsync();
        foreach (var f in folders) Folders.Add(f);

        var albums = await _albumService.GetAlbumsAsync();
        foreach (var a in albums) Albums.Add(a);

        if (App.AppSettings.LastSelectedFolder is { } last && Folders.Contains(last))
            await SelectFolderAsync(last);
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "画像フォルダを選択" };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FolderName;
        await _folderService.RegisterFolderAsync(path);
        if (!Folders.Contains(path)) Folders.Add(path);
        await SelectFolderAsync(path);
    }

    [RelayCommand]
    private async Task RemoveFolder(string path)
    {
        await _folderService.UnregisterFolderAsync(path);
        Folders.Remove(path);
        if (SelectedFolder == path) { AllImages.Clear(); FilteredImages.Clear(); SelectedFolder = null; }
    }

    [RelayCommand]
    private async Task AddAlbum()
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox("アルバム名を入力してください", "新規アルバム");
        if (string.IsNullOrWhiteSpace(name)) return;
        var album = await _albumService.CreateAlbumAsync(name);
        Albums.Add(album);
    }

    public async Task SelectFolderAsync(string path)
    {
        SelectedFolder = path;
        SelectedAlbum = null;
        App.AppSettings.LastSelectedFolder = path;
        await LoadImagesAsync(await _imageService.LoadImagesFromFolderAsync(path, _tagService));
    }

    public async Task SelectAlbumAsync(Album album)
    {
        SelectedAlbum = album;
        SelectedFolder = null;
        await LoadImagesAsync(await _imageService.LoadImagesFromAlbumAsync(album, _tagService));
    }

    private async Task LoadImagesAsync(IReadOnlyList<ImageItem> items)
    {
        IsLoading = true;
        AllImages.Clear();
        FilteredImages.Clear();
        TagFilters.Clear();

        var vms = items.Select(i => new ImageItemViewModel(i)).ToList();
        foreach (var vm in vms) AllImages.Add(vm);

        await RefreshTagFiltersAsync();
        ApplyTagFilter();

        // サムネイルを非同期で生成
        _ = Task.Run(async () =>
        {
            foreach (var vm in vms)
            {
                var bytes = await _imageService.GenerateThumbnailAsync(vm.Model.FilePath);
                Application.Current.Dispatcher.Invoke(() => vm.SetThumbnailFromBytes(bytes));
            }
            Application.Current.Dispatcher.Invoke(() => IsLoading = false);
        });
    }

    private async Task RefreshTagFiltersAsync()
    {
        var tagNames = AllImages
            .SelectMany(vm => vm.Model.Tags.Select(t => t.Name))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        TagFilters.Clear();
        foreach (var name in tagNames)
        {
            var item = new TagFilterItem(name);
            item.SelectionChanged += (_, _) => ApplyTagFilter();
            TagFilters.Add(item);
        }
    }

    public void ApplyTagFilter()
    {
        var selectedTags = TagFilters.Where(t => t.IsSelected).Select(t => t.Name).ToHashSet();
        FilteredImages.Clear();
        foreach (var vm in AllImages)
        {
            if (selectedTags.Count == 0 || vm.Model.Tags.Any(t => selectedTags.Contains(t.Name)))
                FilteredImages.Add(vm);
        }
    }

    private async void OnFolderChanged(object? sender, FolderChangedEventArgs e)
    {
        if (SelectedFolder == e.FolderPath)
            await SelectFolderAsync(e.FolderPath);
        else if (SelectedAlbum?.FolderPaths.Contains(e.FolderPath) == true)
            await SelectAlbumAsync(SelectedAlbum);
    }

    partial void OnThumbnailSizeChanged(double value)
        => App.AppSettings.ThumbnailSize = value;
}
```

**Note:** `OpenFolderDialog` は .NET 8 + WPF 標準。`Microsoft.VisualBasic.Interaction.InputBox` は `Microsoft.VisualBasic` NuGet パッケージが必要（または自作ダイアログで代替）。

- [ ] **Step 3b: Microsoft.VisualBasic を追加する**

```powershell
dotnet add src/PhotoExplorer.App/PhotoExplorer.App.csproj package Microsoft.VisualBasic --version 10.3.*
```

- [ ] **Step 4: MainWindow.xaml を実装する**

`src/PhotoExplorer.App/MainWindow.xaml`:
```xml
<Window x:Class="PhotoExplorer.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:PhotoExplorer.App.Views"
        Title="Photo Explorer" Height="800" Width="1200">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="220" MinWidth="150"/>
            <ColumnDefinition Width="5"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <views:SidebarView Grid.Column="0"/>

        <GridSplitter Grid.Column="1" HorizontalAlignment="Stretch" Background="#DDDDDD"/>

        <Grid Grid.Column="2">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            <views:TagFilterView Grid.Row="0"/>
            <views:ImageGridView Grid.Row="1"/>
            <StatusBar Grid.Row="2">
                <StatusBarItem>
                    <TextBlock>
                        <TextBlock.Text>
                            <MultiBinding StringFormat="{}{0} 枚">
                                <Binding Path="FilteredImages.Count"/>
                            </MultiBinding>
                        </TextBlock.Text>
                    </TextBlock>
                </StatusBarItem>
                <Separator/>
                <StatusBarItem HorizontalAlignment="Right">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="サイズ:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                        <Slider Width="100" Minimum="80" Maximum="300"
                                Value="{Binding ThumbnailSize}"
                                VerticalAlignment="Center"/>
                    </StackPanel>
                </StatusBarItem>
            </StatusBar>
        </Grid>

        <!-- ローディング オーバーレイ -->
        <Grid Grid.Column="2" Background="#80000000"
              Visibility="{Binding IsLoading, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="読み込み中..." Foreground="White"
                       HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="20"/>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 5: MainWindow.xaml.cs を実装する**

`src/PhotoExplorer.App/MainWindow.xaml.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using PhotoExplorer.App.ViewModels;
using System.Windows;

namespace PhotoExplorer.App;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel(
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IFolderService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IAlbumService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IImageService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.ITagService>());
        DataContext = ViewModel;
        RestoreWindowState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.InitializeAsync();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        => SaveWindowState();

    private void RestoreWindowState()
    {
        var s = App.AppSettings;
        Left = s.WindowLeft; Top = s.WindowTop;
        Width = s.WindowWidth; Height = s.WindowHeight;
    }

    private void SaveWindowState()
    {
        App.AppSettings.WindowLeft = Left; App.AppSettings.WindowTop = Top;
        App.AppSettings.WindowWidth = Width; App.AppSettings.WindowHeight = Height;
    }
}
```

XAML の `Window` タグに `Loaded` と `Closing` イベントを追加する:
```xml
<Window ... Loaded="Window_Loaded" Closing="Window_Closing">
```

また、`BoolToVisibilityConverter` を `App.xaml` の `Application.Resources` に追加する:
```xml
<Application.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter"/>
</Application.Resources>
```

- [ ] **Step 6: SidebarView を実装する**

`src/PhotoExplorer.App/Views/SidebarView.xaml`:
```xml
<UserControl x:Class="PhotoExplorer.App.Views.SidebarView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel>
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Margin="4">
            <Button Content="+ フォルダ" Command="{Binding AddFolderCommand}" Margin="0,0,4,0" Padding="6,2"/>
            <Button Content="+ アルバム" Command="{Binding AddAlbumCommand}" Padding="6,2"/>
        </StackPanel>

        <ScrollViewer>
            <StackPanel>
                <!-- フォルダセクション -->
                <TextBlock Text="フォルダ" FontWeight="Bold" Margin="8,8,8,4" Foreground="#555"/>
                <ItemsControl ItemsSource="{Binding Folders}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Background="Transparent" Padding="8,4" Cursor="Hand"
                                    MouseLeftButtonDown="FolderItem_Click">
                                <Border.ContextMenu>
                                    <ContextMenu>
                                        <MenuItem Header="削除" CommandParameter="{Binding}"
                                                  Command="{Binding DataContext.RemoveFolderCommand,
                                                    RelativeSource={RelativeSource AncestorType=ItemsControl}}"/>
                                    </ContextMenu>
                                </Border.ContextMenu>
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="📁 " FontSize="14"/>
                                    <TextBlock Text="{Binding Converter={StaticResource PathToNameConverter}}"
                                               VerticalAlignment="Center"/>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- アルバムセクション -->
                <TextBlock Text="アルバム" FontWeight="Bold" Margin="8,12,8,4" Foreground="#555"/>
                <ItemsControl ItemsSource="{Binding Albums}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Background="Transparent" Padding="8,4" Cursor="Hand"
                                    MouseLeftButtonDown="AlbumItem_Click">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="📒 " FontSize="14"/>
                                    <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`src/PhotoExplorer.App/Views/SidebarView.xaml.cs`:
```csharp
using PhotoExplorer.App.ViewModels;
using PhotoExplorer.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhotoExplorer.App.Views;

public partial class SidebarView : UserControl
{
    public SidebarView() => InitializeComponent();

    private MainViewModel Vm => (MainViewModel)DataContext;

    private async void FolderItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is string path)
            await Vm.SelectFolderAsync(path);
    }

    private async void AlbumItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Album album)
            await Vm.SelectAlbumAsync(album);
    }
}
```

`PathToNameConverter` を `App.xaml` の Resources に追加する（フォルダパスからフォルダ名だけを表示）:

`src/PhotoExplorer.App/Converters/PathToNameConverter.cs`:
```csharp
using System.Globalization;
using System.Windows.Data;

namespace PhotoExplorer.App.Converters;

public class PathToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string path ? Path.GetFileName(path.TrimEnd('\\', '/')) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

`App.xaml` の Resources に追加:
```xml
<Application.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter"/>
    <converters:PathToNameConverter x:Key="PathToNameConverter"
        xmlns:converters="clr-namespace:PhotoExplorer.App.Converters"/>
</Application.Resources>
```

- [ ] **Step 7: ビルドを確認する**

```powershell
dotnet build src/PhotoExplorer.App/
```
Expected: `Build succeeded`（XAML デザインエラーは実行時まで分からない場合がある）

- [ ] **Step 8: コミットする**

```powershell
git add src/PhotoExplorer.App/
git commit -m "feat: add MainViewModel, MainWindow layout, and SidebarView"
```

---

### Task 9: ImageGridView + ドラッグアンドドロップ

**Files:**
- Create: `src/PhotoExplorer.App/Views/ImageGridView.xaml`
- Create: `src/PhotoExplorer.App/Views/ImageGridView.xaml.cs`

**Interfaces:**
- Consumes: `MainViewModel.FilteredImages`, `MainViewModel.ThumbnailSize`
- Produces: サムネイルグリッド表示、D&D（FileDrop 形式）、ダブルクリックで PreviewWindow 起動

- [ ] **Step 1: ImageGridView.xaml を実装する**

`src/PhotoExplorer.App/Views/ImageGridView.xaml`:
```xml
<UserControl x:Class="PhotoExplorer.App.Views.ImageGridView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:PhotoExplorer.App.ViewModels">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <ItemsControl ItemsSource="{Binding FilteredImages}" x:Name="ImageList">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate DataType="{x:Type vm:ImageItemViewModel}">
                    <Border Width="{Binding DataContext.ThumbnailSize,
                                RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                            Height="{Binding DataContext.ThumbnailSize,
                                RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                            BorderBrush="#CCCCCC" BorderThickness="1" Margin="4"
                            Background="#F5F5F5" Cursor="Hand"
                            PreviewMouseLeftButtonDown="Border_PreviewMouseLeftButtonDown"
                            PreviewMouseMove="Border_PreviewMouseMove"
                            MouseDoubleClick="Border_MouseDoubleClick">
                        <Border.ContextMenu>
                            <ContextMenu>
                                <MenuItem Header="タグを追加/編集..."
                                          Click="TagMenuItem_Click"/>
                            </ContextMenu>
                        </Border.ContextMenu>
                        <Grid>
                            <Image Source="{Binding Thumbnail}" Stretch="Uniform"/>
                            <!-- サムネイル未生成時のプレースホルダー -->
                            <TextBlock Text="🖼️" FontSize="32"
                                       HorizontalAlignment="Center" VerticalAlignment="Center"
                                       Visibility="{Binding Thumbnail, Converter={StaticResource NullToVisibilityConverter}}"/>
                            <!-- ファイル名ラベル -->
                            <TextBlock Text="{Binding Model.FileName}"
                                       VerticalAlignment="Bottom" Background="#80000000"
                                       Foreground="White" FontSize="10"
                                       TextTrimming="CharacterEllipsis" Padding="2,1"/>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</UserControl>
```

`NullToVisibilityConverter` を `Converters/NullToVisibilityConverter.cs` に追加する:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoExplorer.App.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value == null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

`App.xaml` の Resources にも追加:
```xml
<converters:NullToVisibilityConverter x:Key="NullToVisibilityConverter"
    xmlns:converters="clr-namespace:PhotoExplorer.App.Converters"/>
```

- [ ] **Step 2: ImageGridView.xaml.cs を実装する（D&D 含む）**

`src/PhotoExplorer.App/Views/ImageGridView.xaml.cs`:
```csharp
using PhotoExplorer.App.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhotoExplorer.App.Views;

public partial class ImageGridView : UserControl
{
    private Point _dragStartPoint;

    public ImageGridView() => InitializeComponent();

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void Border_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStartPoint = e.GetPosition(null);

    private void Border_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (sender is FrameworkElement fe && fe.DataContext is ImageItemViewModel vm)
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { vm.Model.FilePath });
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
        }
    }

    private void Border_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ImageItemViewModel vm)
        {
            var images = Vm.FilteredImages.Select(i => i.Model).ToList();
            var index = images.IndexOf(vm.Model);
            var preview = new PreviewWindow(images, index);
            preview.Owner = Window.GetWindow(this);
            preview.Show();
        }
    }

    private async void TagMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ImageItemViewModel vm)
        {
            var dialog = new TagEditDialog(vm.Model, App.Services.GetRequiredService<PhotoExplorer.Core.Services.ITagService>());
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                vm.Model.Tags = (await App.Services.GetRequiredService<PhotoExplorer.Core.Services.ITagService>()
                    .GetTagsAsync(vm.Model.FilePath)).ToList();
                Vm.ApplyTagFilter();
            }
        }
    }
}
```

**Note:** `TagEditDialog` は Task 10 で作成する。`App.Services.GetRequiredService` は `Microsoft.Extensions.DependencyInjection` の拡張メソッド。`using Microsoft.Extensions.DependencyInjection;` を追加する。

- [ ] **Step 3: ビルドを確認する**

```powershell
dotnet build src/PhotoExplorer.App/
```
Expected: `Build succeeded`

- [ ] **Step 4: コミットする**

```powershell
git add src/PhotoExplorer.App/Views/ImageGridView.xaml src/PhotoExplorer.App/Views/ImageGridView.xaml.cs src/PhotoExplorer.App/Converters/
git commit -m "feat: add ImageGridView with thumbnail display and drag-and-drop"
```

---

### Task 10: TagFilterView + TagEditDialog

**Files:**
- Create: `src/PhotoExplorer.App/Views/TagFilterView.xaml`
- Create: `src/PhotoExplorer.App/Views/TagFilterView.xaml.cs`
- Create: `src/PhotoExplorer.App/Views/TagEditDialog.xaml`
- Create: `src/PhotoExplorer.App/Views/TagEditDialog.xaml.cs`

**Interfaces:**
- Consumes: `MainViewModel.TagFilters`
- Produces: タグチェックボックス一覧（OR 絞込）、タグ追加/削除ダイアログ

- [ ] **Step 1: TagFilterView を実装する**

`src/PhotoExplorer.App/Views/TagFilterView.xaml`:
```xml
<UserControl x:Class="PhotoExplorer.App.Views.TagFilterView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:PhotoExplorer.App.ViewModels">
    <Border BorderBrush="#DDDDDD" BorderThickness="0,0,0,1" Padding="8,4">
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="タグ絞込:" VerticalAlignment="Center" Margin="0,0,8,0" FontSize="12"/>
            <ItemsControl ItemsSource="{Binding TagFilters}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel/>
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:TagFilterItem}">
                        <CheckBox Content="{Binding Name}"
                                  IsChecked="{Binding IsSelected}"
                                  Margin="4,2" Padding="4,1"
                                  VerticalAlignment="Center"/>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock Text="(タグなし)" VerticalAlignment="Center" Foreground="#999"
                       Visibility="{Binding TagFilters.Count,
                           Converter={StaticResource ZeroToVisibilityConverter}}"/>
        </StackPanel>
    </Border>
</UserControl>
```

`Converters/ZeroToVisibilityConverter.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoExplorer.App.Converters;

public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

`App.xaml` Resources に追加:
```xml
<converters:ZeroToVisibilityConverter x:Key="ZeroToVisibilityConverter"
    xmlns:converters="clr-namespace:PhotoExplorer.App.Converters"/>
```

`src/PhotoExplorer.App/Views/TagFilterView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace PhotoExplorer.App.Views;

public partial class TagFilterView : UserControl
{
    public TagFilterView() => InitializeComponent();
}
```

- [ ] **Step 2: TagEditDialog を実装する**

`src/PhotoExplorer.App/Views/TagEditDialog.xaml`:
```xml
<Window x:Class="PhotoExplorer.App.Views.TagEditDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="タグの編集" Width="320" Height="300"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="{Binding FileName}" FontWeight="Bold" Margin="0,0,0,8"
                   TextTrimming="CharacterEllipsis"/>

        <ListBox Grid.Row="1" ItemsSource="{Binding Tags}" x:Name="TagList" Margin="0,0,0,8">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding}" VerticalAlignment="Center" Margin="0,0,8,0"/>
                        <Button Content="×" Tag="{Binding}" Click="RemoveTag_Click"
                                Padding="4,1" Background="Transparent" BorderThickness="0"
                                Foreground="Red" Cursor="Hand"/>
                    </StackPanel>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <Grid Grid.Row="2" Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="NewTagBox" Grid.Column="0" Margin="0,0,4,0"
                     KeyDown="NewTagBox_KeyDown"/>
            <Button Grid.Column="1" Content="追加" Click="AddTag_Click" Padding="8,2"/>
        </Grid>

        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="閉じる" IsDefault="True" Click="Close_Click" Padding="12,4"/>
        </StackPanel>
    </Grid>
</Window>
```

`src/PhotoExplorer.App/Views/TagEditDialog.xaml.cs`:
```csharp
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace PhotoExplorer.App.Views;

public partial class TagEditDialog : Window
{
    private readonly ImageItem _item;
    private readonly ITagService _tagService;

    public string FileName => _item.FileName;
    public ObservableCollection<string> Tags { get; } = new();

    public TagEditDialog(ImageItem item, ITagService tagService)
    {
        _item = item;
        _tagService = tagService;
        DataContext = this;
        InitializeComponent();
        foreach (var t in item.Tags) Tags.Add(t.Name);
    }

    private async void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = NewTagBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || Tags.Contains(name)) return;
        await _tagService.AddTagAsync(_item.FilePath, name);
        Tags.Add(name);
        NewTagBox.Clear();
        DialogResult = true;
    }

    private async void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tagName)
        {
            await _tagService.RemoveTagAsync(_item.FilePath, tagName);
            Tags.Remove(tagName);
            DialogResult = true;
        }
    }

    private void NewTagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTag_Click(sender, e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 3: ビルドを確認する**

```powershell
dotnet build src/PhotoExplorer.App/
```
Expected: `Build succeeded`

- [ ] **Step 4: コミットする**

```powershell
git add src/PhotoExplorer.App/Views/TagFilterView.xaml src/PhotoExplorer.App/Views/TagFilterView.xaml.cs src/PhotoExplorer.App/Views/TagEditDialog.xaml src/PhotoExplorer.App/Views/TagEditDialog.xaml.cs src/PhotoExplorer.App/Converters/
git commit -m "feat: add TagFilterView and TagEditDialog"
```

---

### Task 11: PreviewWindow (フローティングプレビュー)

**Files:**
- Create: `src/PhotoExplorer.App/ViewModels/PreviewViewModel.cs`
- Create: `src/PhotoExplorer.App/PreviewWindow.xaml`
- Create: `src/PhotoExplorer.App/PreviewWindow.xaml.cs`

**Interfaces:**
- Consumes: `List<ImageItem>`, `int initialIndex`
- Produces: フローティングウィンドウ。`←` `→` キーで画像切替、`ESC` で閉じる。

- [ ] **Step 1: PreviewViewModel を実装する**

`src/PhotoExplorer.App/ViewModels/PreviewViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoExplorer.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace PhotoExplorer.App.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    private readonly IReadOnlyList<ImageItem> _images;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private BitmapSource? _currentImage;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _currentTags = string.Empty;

    public bool CanGoPrevious => CurrentIndex > 0;
    public bool CanGoNext => CurrentIndex < _images.Count - 1;

    public PreviewViewModel(IReadOnlyList<ImageItem> images, int initialIndex)
    {
        _images = images;
        Navigate(initialIndex);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Navigate(CurrentIndex - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Navigate(CurrentIndex + 1);

    private void Navigate(int index)
    {
        if (index < 0 || index >= _images.Count) return;
        CurrentIndex = index;
        var item = _images[index];
        CurrentFileName = item.FileName;
        CurrentTags = item.Tags.Count > 0
            ? string.Join("  ", item.Tags.Select(t => $"[{t.Name}]"))
            : "(タグなし)";

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(item.FilePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            CurrentImage = bitmap;
        }
        catch { CurrentImage = null; }

        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }
}
```

- [ ] **Step 2: PreviewWindow.xaml を実装する**

`src/PhotoExplorer.App/PreviewWindow.xaml`:
```xml
<Window x:Class="PhotoExplorer.App.PreviewWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{Binding CurrentFileName}"
        Width="900" Height="700" MinWidth="400" MinHeight="300"
        Background="#1E1E1E"
        KeyDown="Window_KeyDown">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- ツールバー -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Background="#2D2D2D" Height="36">
            <Button Content="◀" Command="{Binding PreviousCommand}"
                    Width="36" Background="Transparent" BorderThickness="0"
                    Foreground="White" FontSize="16"/>
            <TextBlock Text="{Binding CurrentFileName}"
                       Foreground="White" VerticalAlignment="Center"
                       Margin="8,0" TextTrimming="CharacterEllipsis" MaxWidth="600"/>
            <Button Content="▶" Command="{Binding NextCommand}"
                    Width="36" Background="Transparent" BorderThickness="0"
                    Foreground="White" FontSize="16"/>
        </StackPanel>

        <!-- 画像表示 -->
        <Image Grid.Row="1" Source="{Binding CurrentImage}"
               Stretch="Uniform" Margin="8"/>

        <!-- タグ表示 -->
        <Border Grid.Row="2" Background="#2D2D2D" Padding="8,4">
            <TextBlock Text="{Binding CurrentTags}" Foreground="#AAAAAA" FontSize="12"/>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 3: PreviewWindow.xaml.cs を実装する**

`src/PhotoExplorer.App/PreviewWindow.xaml.cs`:
```csharp
using PhotoExplorer.App.ViewModels;
using PhotoExplorer.Core.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace PhotoExplorer.App;

public partial class PreviewWindow : Window
{
    private readonly PreviewViewModel _vm;

    public PreviewWindow(IReadOnlyList<ImageItem> images, int initialIndex)
    {
        InitializeComponent();
        _vm = new PreviewViewModel(images, initialIndex);
        DataContext = _vm;
        RestorePosition();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: _vm.PreviousCommand.Execute(null); break;
            case Key.Right: _vm.NextCommand.Execute(null); break;
            case Key.Escape: Close(); break;
        }
    }

    private void RestorePosition()
    {
        // プレビューウィンドウの位置はメインウィンドウの右側に配置
        if (Owner != null)
        {
            Left = Owner.Left + Owner.Width + 8;
            Top = Owner.Top;
        }
    }
}
```

- [ ] **Step 4: ビルドを確認する**

```powershell
dotnet build src/PhotoExplorer.App/
```
Expected: `Build succeeded`

- [ ] **Step 5: コミットする**

```powershell
git add src/PhotoExplorer.App/ViewModels/PreviewViewModel.cs src/PhotoExplorer.App/PreviewWindow.xaml src/PhotoExplorer.App/PreviewWindow.xaml.cs
git commit -m "feat: add PreviewWindow with left/right key navigation"
```

---

### Task 12: アルバム フォルダ管理 UI

**Files:**
- Modify: `src/PhotoExplorer.App/Views/SidebarView.xaml`
- Modify: `src/PhotoExplorer.App/Views/SidebarView.xaml.cs`
- Modify: `src/PhotoExplorer.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IAlbumService.AddFolderToAlbumAsync`, `IAlbumService.RemoveFolderFromAlbumAsync`, `MainViewModel.Folders`
- Produces: アルバム右クリック → 「フォルダを追加/削除」ダイアログ

- [ ] **Step 1: MainViewModel にアルバムフォルダ管理コマンドを追加する**

`src/PhotoExplorer.App/ViewModels/MainViewModel.cs` の `AddAlbum` メソッドの後に追加する:
```csharp
[RelayCommand]
private async Task AddFolderToAlbum(Album album)
{
    if (Folders.Count == 0)
    {
        MessageBox.Show("先にフォルダを追加してください。", "Photo Explorer");
        return;
    }
    var dialog = new AlbumFolderDialog(album, Folders.ToList(), _albumService);
    dialog.ShowDialog();
    // アルバムのフォルダリストを更新
    var updated = (await _albumService.GetAlbumsAsync()).FirstOrDefault(a => a.Id == album.Id);
    if (updated != null)
    {
        var idx = Albums.IndexOf(album);
        if (idx >= 0) Albums[idx] = updated;
    }
}
```

- [ ] **Step 2: AlbumFolderDialog を作成する**

`src/PhotoExplorer.App/Views/AlbumFolderDialog.xaml`:
```xml
<Window x:Class="PhotoExplorer.App.Views.AlbumFolderDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{Binding Title}" Width="420" Height="320"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="このアルバムに含めるフォルダを選択してください"
                   Margin="0,0,0,8" TextWrapping="Wrap"/>

        <ListBox Grid.Row="1" ItemsSource="{Binding FolderItems}" Margin="0,0,0,8">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <CheckBox IsChecked="{Binding IsIncluded}" Margin="2">
                        <TextBlock Text="{Binding Path}" TextTrimming="CharacterEllipsis" MaxWidth="340"/>
                    </CheckBox>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <Button Grid.Row="2" Content="保存" HorizontalAlignment="Right"
                Click="Save_Click" Padding="16,4"/>
    </Grid>
</Window>
```

`src/PhotoExplorer.App/Views/AlbumFolderDialog.xaml.cs`:
```csharp
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace PhotoExplorer.App.Views;

public partial class AlbumFolderDialog : Window
{
    private readonly Album _album;
    private readonly IAlbumService _albumService;

    public string Title => $"アルバム: {_album.Name}";
    public ObservableCollection<AlbumFolderItem> FolderItems { get; } = new();

    public AlbumFolderDialog(Album album, IList<string> allFolders, IAlbumService albumService)
    {
        _album = album;
        _albumService = albumService;
        DataContext = this;
        InitializeComponent();
        foreach (var f in allFolders)
            FolderItems.Add(new AlbumFolderItem(f, album.FolderPaths.Contains(f)));
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in FolderItems)
        {
            if (item.IsIncluded && !_album.FolderPaths.Contains(item.Path))
                await _albumService.AddFolderToAlbumAsync(_album.Id, item.Path);
            else if (!item.IsIncluded && _album.FolderPaths.Contains(item.Path))
                await _albumService.RemoveFolderFromAlbumAsync(_album.Id, item.Path);
        }
        DialogResult = true;
    }
}

public class AlbumFolderItem : INotifyPropertyChanged
{
    public string Path { get; }
    private bool _isIncluded;
    public bool IsIncluded
    {
        get => _isIncluded;
        set { _isIncluded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public AlbumFolderItem(string path, bool isIncluded) { Path = path; _isIncluded = isIncluded; }
}
```

- [ ] **Step 3: SidebarView のアルバムコンテキストメニューを更新する**

`SidebarView.xaml` のアルバム `ItemsControl.ItemTemplate` を以下に書き換える:
```xml
<ItemsControl.ItemTemplate>
    <DataTemplate>
        <Border Background="Transparent" Padding="8,4" Cursor="Hand"
                MouseLeftButtonDown="AlbumItem_Click">
            <Border.ContextMenu>
                <ContextMenu>
                    <MenuItem Header="フォルダを追加/削除..."
                              CommandParameter="{Binding}"
                              Command="{Binding DataContext.AddFolderToAlbumCommand,
                                RelativeSource={RelativeSource AncestorType=ItemsControl}}"/>
                    <Separator/>
                    <MenuItem Header="削除"
                              CommandParameter="{Binding Id}"
                              Command="{Binding DataContext.DeleteAlbumCommand,
                                RelativeSource={RelativeSource AncestorType=ItemsControl}}"/>
                </ContextMenu>
            </Border.ContextMenu>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="📒 " FontSize="14"/>
                <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
            </StackPanel>
        </Border>
    </DataTemplate>
</ItemsControl.ItemTemplate>
```

- [ ] **Step 4: MainViewModel に DeleteAlbumCommand を追加する**

`MainViewModel.cs` に追加する:
```csharp
[RelayCommand]
private async Task DeleteAlbum(int albumId)
{
    var album = Albums.FirstOrDefault(a => a.Id == albumId);
    if (album == null) return;
    await _albumService.DeleteAlbumAsync(albumId);
    Albums.Remove(album);
    if (SelectedAlbum?.Id == albumId) { AllImages.Clear(); FilteredImages.Clear(); SelectedAlbum = null; }
}
```

- [ ] **Step 5: ビルドを確認する**

```powershell
dotnet build src/PhotoExplorer.App/
```
Expected: `Build succeeded`

- [ ] **Step 6: コミットする**

```powershell
git add src/PhotoExplorer.App/
git commit -m "feat: add album folder management UI"
```

---

### Task 13: 全体結合テスト (手動)

サービステストは全て通っているはずだが、UI 全体を実際に起動して動作確認する。

- [ ] **Step 1: アプリを起動する**

```powershell
dotnet run --project src/PhotoExplorer.App/
```
Expected: Photo Explorer ウィンドウが表示される

- [ ] **Step 2: フォルダ追加を確認する**

1. 「+ フォルダ」をクリック
2. 画像が入ったフォルダを選択
3. サムネイルグリッドに画像が表示されることを確認

- [ ] **Step 3: D&D を確認する**

1. グリッドから画像をエクスプローラーウィンドウにドラッグ
2. ファイルがコピーできることを確認（Affinity がインストール済みなら Affinity でも確認）

- [ ] **Step 4: タグ付けを確認する**

1. 画像を右クリック → 「タグを追加/編集...」
2. タグ名を入力して追加
3. タグフィルタバーにタグが表示されることを確認
4. タグにチェックを入れて絞込が動作することを確認

- [ ] **Step 5: プレビューを確認する**

1. 画像をダブルクリック → PreviewWindow が開くことを確認
2. ←→ キーで画像が切り替わることを確認
3. ESC で閉じることを確認

- [ ] **Step 6: ファイル監視を確認する**

1. 登録済みフォルダにファイルをコピー
2. 自動でグリッドに追加されることを確認

- [ ] **Step 7: アルバム作成を確認する**

1. 「+ アルバム」をクリック
2. アルバム名を入力
3. サイドバーにアルバムが表示されることを確認
4. アルバムを右クリック → 「フォルダを追加」で登録済みフォルダを選択できることを確認

- [ ] **Step 8: 再起動後の状態復元を確認する**

1. アプリを閉じて再起動
2. 前回のフォルダが自動で読み込まれることを確認
3. ウィンドウ位置が復元されることを確認

- [ ] **Step 9: 全テストが通ることを確認する**

```powershell
dotnet test
```
Expected: All PASS

- [ ] **Step 10: 最終コミットする**

```powershell
git add .
git commit -m "feat: photo explorer MVP complete"
```
