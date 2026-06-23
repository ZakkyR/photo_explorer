using System.IO;
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
        dbContext.Database.Migrate();

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
