using System.IO;
using Microsoft.Data.Sqlite;
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
        // DBがEnsureCreated()で作成された場合、InitialCreateを適用済みとしてマークする
        // テーブル存在チェックではなく、レコード存在チェックで判断する
        if (File.Exists(dbPath))
        {
            using var rawConn = new SqliteConnection($"Data Source={dbPath}");
            rawConn.Open();
            bool initialCreateApplied;
            using (var checkCmd = rawConn.CreateCommand())
            {
                try
                {
                    checkCmd.CommandText = "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260623060418_InitialCreate'";
                    initialCreateApplied = (long)checkCmd.ExecuteScalar()! > 0;
                }
                catch { initialCreateApplied = false; }
            }
            if (!initialCreateApplied)
            {
                using var createCmd = rawConn.CreateCommand();
                createCmd.CommandText = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)";
                createCmd.ExecuteNonQuery();
                using var insertCmd = rawConn.CreateCommand();
                insertCmd.CommandText = "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" VALUES ('20260623060418_InitialCreate', '8.0.0')";
                insertCmd.ExecuteNonQuery();
            }
        }

        var dbContext = new AppDbContext(dbOptions);
        dbContext.Database.Migrate();

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

        // 既存データの移行をバックグラウンドで実行（初回のみ）
        var migrationFlag = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhotoExplorer", "migration_v1.done");
        _ = Task.Run(async () =>
        {
            if (File.Exists(migrationFlag)) return;
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
                await sidecarSvc.WriteInitialTagsAsync(
                    group.Key,
                    group.Select(t => (Path.GetFileName(t.FilePath), t.TagName)).ToList());
            }
            File.WriteAllText(migrationFlag, DateTime.UtcNow.ToString("O"));
        });

        Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppSettings.Save();
        (Services.GetService<ISidecarService>() as IDisposable)?.Dispose();
        (Services.GetService<IFolderService>() as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
