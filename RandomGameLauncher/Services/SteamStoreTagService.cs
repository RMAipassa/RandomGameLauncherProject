using System.Net.Http;
using System.Text.Json;

namespace RandomGameLauncher.Services;

public static class SteamStoreTagService
{
    static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "Steam Achievements",
        "Steam Cloud",
        "Steam Leaderboards",
        "Steam Trading Cards",
        "Steam Workshop",
        "Steam Turn Notifications",
        "Remote Play on Phone",
        "Remote Play on Tablet",
        "Remote Play on TV",
        "Remote Play Together",
        "Family Sharing"
    };

    public static async Task<IReadOnlyList<string>> FetchStoreTagsAndGenresAsync(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return Array.Empty<string>();
        if (!int.TryParse(appId, out _)) return Array.Empty<string>();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        var list = new List<string>();

        // Genres are exposed via appdetails.
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(appId)}&l=english";
            using var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (doc.RootElement.TryGetProperty(appId, out var root) &&
                root.TryGetProperty("success", out var successEl) && successEl.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in genres.EnumerateArray())
                    {
                        if (g.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                            list.Add(d.GetString() ?? "");
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        // Store tags are available via apphoverpublic.
        try
        {
            var url = $"https://store.steampowered.com/apphoverpublic/{Uri.EscapeDataString(appId)}?l=english";
            using var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.Number && s.GetInt32() == 1)
            {
                if (doc.RootElement.TryGetProperty("tags", out var tags))
                {
                    foreach (var t in ReadTagStrings(tags))
                        list.Add(t);
                }
            }
        }
        catch
        {
            // ignore
        }

        list = list.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Where(s => !Ignore.Contains(s))
            .ToList();

        return TagService.NormalizeTags(string.Join(',', list));
    }

    static IEnumerable<string> ReadTagStrings(JsonElement el)
    {
        // Possible shapes observed in the wild:
        // - array of strings
        // - object: { "RPG": 123, "Co-op": 45 }
        // - array of objects with { "name": "RPG" }
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in el.EnumerateArray())
            {
                if (x.ValueKind == JsonValueKind.String)
                {
                    yield return x.GetString() ?? "";
                }
                else if (x.ValueKind == JsonValueKind.Object)
                {
                    if (x.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        yield return n.GetString() ?? "";
                }
            }
            yield break;
        }

        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
                yield return p.Name;
        }
    }
}
