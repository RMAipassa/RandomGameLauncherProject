using System.Net.Http;
using System.Text.Json;

namespace RandomGameLauncher.Services;

public static class SteamPlaytimeService
{
    public static async Task<Dictionary<string, double>> FetchPlaytimeHoursAsync(string apiKey, string steamId64)
    {
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamId64))
            return dict;

        var url =
            $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&include_appinfo=0&include_played_free_games=1&format=json";

        using var http = new HttpClient();
        var json = await http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var resp)) return dict;
        if (!resp.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array) return dict;

        foreach (var g in games.EnumerateArray())
        {
            if (!g.TryGetProperty("appid", out var appIdEl)) continue;
            if (!g.TryGetProperty("playtime_forever", out var ptEl)) continue;

            var appId = appIdEl.GetInt32().ToString();
            var minutes = ptEl.GetInt32();
            dict[appId] = minutes / 60.0;
        }

        return dict;
    }
}
