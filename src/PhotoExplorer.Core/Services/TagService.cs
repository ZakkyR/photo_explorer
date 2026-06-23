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
            return iptc.GetValues(IptcTag.Keywords)
                .Select(v => new Tag(v.Value))
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
            var existing = iptc.GetValues(IptcTag.Keywords)
                .Select(v => v.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (add) existing.Add(tagName);
            else existing.Remove(tagName);

            iptc.RemoveValue(IptcTag.Keywords);
            foreach (var kw in existing) iptc.SetValue(IptcTag.Keywords, kw, strict: false);
            image.Metadata.IptcProfile = iptc;
            await image.SaveAsync(filePath);
            return true;
        }
        catch { return false; }
    }
}
