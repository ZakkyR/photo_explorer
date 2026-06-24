using MetadataExtractor.Formats.Iptc;
using ImageMetadataReader = MetadataExtractor.ImageMetadataReader;
using MdStringValue = MetadataExtractor.StringValue;
using Microsoft.EntityFrameworkCore;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Data;

namespace PhotoExplorer.Core.Services;

public class TagService : ITagService
{
    private static readonly HashSet<string> IptcReadSupported =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    private readonly AppDbContext _ctx;

    public TagService(AppDbContext ctx) => _ctx = ctx;

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

    // フォルダ読み込み用: ADO.NET 直接 SQL + IPTC 並列読み
    // EF Core の LINQ Contains クエリは初回コンパイルが極端に遅いため ADO.NET を使う
    public async Task<Dictionary<string, List<Tag>>> GetTagsBulkAsync(IReadOnlyList<string> filePaths)
    {
        var result = filePaths.ToDictionary(
            f => f,
            _ => new List<Tag>(),
            StringComparer.OrdinalIgnoreCase);

        if (filePaths.Count == 0) return result;

        // SQLite: ADO.NET で直接クエリ（EF Core LINQ コンパイルを回避）
        var conn = _ctx.Database.GetDbConnection();
        await _ctx.Database.OpenConnectionAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            var placeholders = string.Join(",", filePaths.Select((_, i) => $"@p{i}"));
            cmd.CommandText = $"SELECT FilePath, TagName FROM ImageTags WHERE FilePath IN ({placeholders})";
            for (int i = 0; i < filePaths.Count; i++)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = $"@p{i}";
                p.Value = filePaths[i];
                cmd.Parameters.Add(p);
            }
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fp = reader.GetString(0);
                var tagName = reader.GetString(1);
                if (result.TryGetValue(fp, out var list))
                    list.Add(new Tag(tagName));
            }
        }
        finally
        {
            _ctx.Database.CloseConnection();
        }

        // IPTC: Task.WhenAll で並列読み取り
        var iptcFiles = filePaths
            .Where(f => IptcReadSupported.Contains(Path.GetExtension(f)))
            .ToList();

        if (iptcFiles.Count > 0)
        {
            var iptcAll = await Task.WhenAll(
                iptcFiles.Select(async f => (f, tags: await ReadIptcKeywordsAsync(f))));

            foreach (var (file, tags) in iptcAll)
            {
                if (!result.TryGetValue(file, out var list)) continue;
                var existing = list.Select(t => t.Name)
                                   .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var t in tags)
                    if (existing.Add(t.Name))
                        list.Add(t);
            }
        }

        foreach (var list in result.Values)
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return result;
    }

    // LINQ コンパイルを避けるため生 SQL を使用（初回から即時完了）
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
    }

    // 複数ファイルへの一括追加: 1トランザクションで N 件まとめて書き込む
    public async Task AddTagBulkAsync(IReadOnlyList<string> filePaths, string tagName)
    {
        if (filePaths.Count == 0) return;
        await using var tx = await _ctx.Database.BeginTransactionAsync();
        foreach (var fp in filePaths)
            await _ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO ImageTags (FilePath, TagName) SELECT {fp}, {tagName} WHERE NOT EXISTS (SELECT 1 FROM ImageTags WHERE FilePath = {fp} AND TagName = {tagName})");
        await tx.CommitAsync();
    }

    public async Task RemoveTagAsync(string filePath, string tagName)
    {
        await _ctx.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM ImageTags WHERE FilePath = {filePath} AND TagName = {tagName}");
    }

    // 複数ファイルからの一括削除: 1トランザクションでまとめて削除
    public async Task RemoveTagBulkAsync(IReadOnlyList<string> filePaths, string tagName)
    {
        if (filePaths.Count == 0) return;
        await using var tx = await _ctx.Database.BeginTransactionAsync();
        foreach (var fp in filePaths)
            await _ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM ImageTags WHERE FilePath = {fp} AND TagName = {tagName}");
        await tx.CommitAsync();
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
