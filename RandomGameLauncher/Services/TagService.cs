using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public static class TagService
{
    public static IReadOnlyList<string> NormalizeTags(string? csvOrSpaced)
    {
        if (string.IsNullOrWhiteSpace(csvOrSpaced)) return Array.Empty<string>();

        var parts = csvOrSpaced
            .Split(new[] { ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => p.TrimStart('#'))
            .Select(p => p.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts;
    }

    public static IReadOnlyList<string> GetTags(Config cfg, GameEntry g)
    {
        if (cfg.TagsByGameKey.TryGetValue(g.Key, out var list) && list is not null)
            return NormalizeTags(string.Join(',', list));
        return Array.Empty<string>();
    }

    public static IReadOnlyList<string> GetAutoTags(Config cfg, GameEntry g)
    {
        if (cfg.AutoTagsByGameKey.TryGetValue(g.Key, out var list) && list is not null)
            return NormalizeTags(string.Join(',', list));
        return Array.Empty<string>();
    }

    public static void SetTags(Config cfg, GameEntry g, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            cfg.TagsByGameKey.Remove(g.Key);
            g.Tags = Array.Empty<string>();
            return;
        }

        var norm = tags.Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
        cfg.TagsByGameKey[g.Key] = norm.ToList();
        g.Tags = norm;
    }

    public static void SetAutoTags(Config cfg, GameEntry g, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            cfg.AutoTagsByGameKey.Remove(g.Key);
            g.AutoTags = Array.Empty<string>();
            return;
        }

        var norm = tags.Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
        cfg.AutoTagsByGameKey[g.Key] = norm.ToList();
        g.AutoTags = norm;
    }
}
