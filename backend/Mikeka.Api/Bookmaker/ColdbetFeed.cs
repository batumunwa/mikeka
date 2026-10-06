using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Mikeka.Api.Bookmaker;

/// <summary>
/// Coldbet's own odds data ("LineFeed", plain JSON): the same leagues, matches and odds the site draws on its canvas.
/// Fetched from inside the logged-in tab, so it is what this account sees. Field meanings, checked on 2026-10-06:
/// leagues LI = id, L = name; matches I = id, CI = id in the match page address, O1/O2 = teams, S = kickoff (unix, UTC),
/// SG = sub-markets (TG "Corners", "Yellow Cards", …; PN "1st half" etc. for periods);
/// market groups GE: G 17 = Total, outcome T 9 = Over, T 10 = Under, P = line, C = odds.
/// </summary>
public static class ColdbetFeed
{
    public const string Path = "/service-api/LineFeed/";
    private const int TotalGroup = 17, OverType = 9, UnderType = 10;

    public record League(long Id, string Name);
    public record Game(long Id, long PageId, string Home, string Away, DateTime KickoffUtc, IReadOnlyList<SubGame> SubGames);
    public record SubGame(long Id, string Name, string Period);

    private static async Task<JsonElement> GetAsync(IPage page, string query)
    {
        var text = await page.EvaluateAsync<string>(
            "async url => { const r = await fetch(new URL(url, location.origin), { credentials: 'include' }); if (!r.ok) throw new Error('HTTP ' + r.status); return await r.text(); }",
            Path + query);
        var root = JsonDocument.Parse(text).RootElement;
        if (root.TryGetProperty("Success", out var ok) && ok.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException($"Coldbet data request {query.Split('?')[0]} failed: {Str(root, "Error")}");
        return root.GetProperty("Value").Clone();
    }

    /// <summary>All football leagues on offer now.</summary>
    public static async Task<List<League>> LeaguesAsync(IPage page)
    {
        var list = new List<League>();
        foreach (var c in (await GetAsync(page, "GetChampsZip?sport=1&lng=en")).EnumerateArray())
            list.Add(new League(Num(c, "LI"), Str(c, "L")));
        return list;
    }

    /// <summary>A league's matches (placeholder "Home v Away" rows for special bets included; callers skip them).</summary>
    public static async Task<List<Game>> GamesAsync(IPage page, long leagueId)
    {
        var v = await GetAsync(page, $"GetChampZip?champ={leagueId}&lng=en");
        var list = new List<Game>();
        if (!v.TryGetProperty("G", out var games)) return list;
        foreach (var g in games.EnumerateArray())
        {
            var subs = new List<SubGame>();
            if (g.TryGetProperty("SG", out var sg))
                foreach (var s in sg.EnumerateArray())
                    subs.Add(new SubGame(Num(s, "I"), Str(s, "TG"), Str(s, "PN")));
            list.Add(new Game(Num(g, "I"), Num(g, "CI"), Str(g, "O1"), Str(g, "O2"),
                DateTimeOffset.FromUnixTimeSeconds(Num(g, "S")).UtcDateTime, subs));
        }
        return list;
    }

    /// <summary>Over/Under rows of the Total market of a match or one of its sub-markets (corners, cards…).</summary>
    public static async Task<List<(string side, decimal line, decimal odds)>> TotalsAsync(IPage page, long gameId)
    {
        var v = await GetAsync(page, $"GetGameZip?id={gameId}&lng=en&isSubGames=true&GroupEvents=true&countevents=250&grMode=4&partner=0&topGroups=&marketType=1");
        var rows = new List<(string, decimal, decimal)>();
        if (!v.TryGetProperty("GE", out var groups)) return rows;
        foreach (var ge in groups.EnumerateArray())
        {
            if (Num(ge, "G") != TotalGroup) continue;
            foreach (var column in ge.GetProperty("E").EnumerateArray())
                foreach (var e in column.EnumerateArray())
                {
                    var t = Num(e, "T");
                    if ((t != OverType && t != UnderType) || !e.TryGetProperty("P", out var p) || !e.TryGetProperty("C", out var c)) continue;
                    rows.Add((t == UnderType ? "Under" : "Over", p.GetDecimal(), c.GetDecimal()));
                }
        }
        return rows;
    }

    /// <summary>Site address part from a name: "UEFA Nations League" → "uefa-nations-league".</summary>
    public static string Slug(string s) => Regex.Replace(Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-"), "^-|-$", "");

    /// <summary>A league name as the account writes it ("Argentina. Primera Division") against the site's, ignoring case and spacing.</summary>
    public static bool SameLeague(string a, string b) =>
        Regex.Replace(a, @"\s+", " ").Trim().Equals(Regex.Replace(b, @"\s+", " ").Trim(), StringComparison.OrdinalIgnoreCase)
        || Regex.Replace(a, @"[\s.]+", "").Equals(Regex.Replace(b, @"[\s.]+", ""), StringComparison.OrdinalIgnoreCase);

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static long Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (v.TryGetInt64(out var l) ? l : (long)v.GetDouble()) : 0;
}
