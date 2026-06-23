using MetadataExtractor.Formats.Iptc;
using ImageMetadataReader = MetadataExtractor.ImageMetadataReader;
using MdStringValue = MetadataExtractor.StringValue;
using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Data;
using PhotoExplorer.Data.Entities;

namespace PhotoExplorer.Core.Services;

public class TagService : ITagService
{
    private static readonly HashSet<string> IptcReadSupported =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    private readonly AppDbContext _ctx;

    public TagService(AppDbContext ctx) => _ctx = ctx;

    // IPTC（読み取り専用）と SQLite（読み書き）をマージして返す。
    // JPEG 全体再エンコードが必要な IPTC 書き込みは行わず、SQLite を正とする。
    public async Task<IReadOnlyList<Tag>> GetTagsAsync(string filePath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (IptcReadSupported.Contains(Path.GetExtension(filePath)))
        {
            var iptc = await ReadIptcKeywordsAsync(filePath);
            foreach (var t in iptc) names.Add(t.Name);
        }

        var dbTags = await _ctx.ImageTags
            .Where(t => t.FilePath == filePath)
            .Select(t => t.TagName)
            .ToListAsync();
        foreach (var t in dbTags) names.Add(t);

        return names.OrderBy(n => n).Select(n => new Tag(n)).ToList();
    }

    public async Task AddTagAsync(string filePath, string tagName)
    {
        if (!await _ctx.ImageTags.AnyAsync(t => t.FilePath == filePath && t.TagName == tagName))
        {
            _ctx.ImageTags.Add(new ImageTagEntity { FilePath = filePath, TagName = tagName });
            await _ctx.SaveChangesAsync();
        }
    }

    public async Task RemoveTagAsync(string filePath, string tagName)
    {
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

    private static Task<IReadOnlyList<Tag>> ReadIptcKeywordsAsync(string filePath)
    {
        return Task.Run<IReadOnlyList<Tag>>(() =>
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();
                if (iptc == null) return Array.Empty<Tag>();

                var raw = iptc.GetObject(IptcDirectory.TagKeywords);
                IEnumerable<string> keywords = raw switch
                {
                    MdStringValue[] arr => arr.Select(sv => sv.ToString()),
                    MdStringValue sv    => new[] { sv.ToString() },
                    string[] arr        => arr,
                    string s            => new[] { s },
                    _                   => Array.Empty<string>()
                };
                return keywords
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => new Tag(k))
                    .ToList();
            }
            catch { return Array.Empty<Tag>(); }
        });
    }
}
