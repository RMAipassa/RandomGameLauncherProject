using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public static class WeightedPicker
{
    public static GameEntry? Pick(IReadOnlyList<GameEntry> items, bool usePlaytimeWeighting)
    {
        if (items.Count == 0) return null;
        if (!usePlaytimeWeighting) return items[Random.Shared.Next(items.Count)];

        var weights = new double[items.Count];
        double total = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var g = items[i];
            var hours = g.Platform == "steam" ? (g.PlaytimeHours ?? 0) : 0;
            var w = Math.Sqrt(hours + 1.0);
            if (w <= 0) w = 1;
            weights[i] = w;
            total += w;
        }

        var r = Random.Shared.NextDouble() * total;
        double acc = 0;

        for (int i = 0; i < items.Count; i++)
        {
            acc += weights[i];
            if (r <= acc) return items[i];
        }

        return items[^1];
    }
}
