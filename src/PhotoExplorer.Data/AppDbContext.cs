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
