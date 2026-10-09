namespace Mikeka.Api.Services;

/// <summary>A match as read from the bookmaker, with its total cards / total corners selections.</summary>
public record MatchInfo(string Id, string League, string Home, string Away, DateTime Kickoff, IReadOnlyList<Selection> Selections);

/// <summary>
/// One clickable outcome, e.g. Total Corners Under 9.5 @ 1.15, or (with Interval "16-30") "no goal in minutes 16–30" @ 1.18.
/// Ref is whatever the bookmaker client needs to click it again.
/// </summary>
public record Selection(string Market, string Side, decimal Line, decimal Odds, string Ref, string? Interval = null, string? Label = null);

public record Pick(MatchInfo Match, Selection Selection)
{
    public decimal Odds => Selection.Odds;
}

/// <summary>
/// A pick already riding on an open slip (any account, any site), e.g. Man United v Chelsea, family "goal", range "1-10".
/// The same match is never bet again with the same market family and minute range while it is open.
/// </summary>
public record TakenPick(string Home, string Away, DateTime Kickoff, string Family, string Range, string Where);

public record SlipPlan(IReadOnlyList<Pick> Picks, decimal CombinedOdds);

/// <summary>What the system decided for one match, with the reasons in order.</summary>
public record MatchDecision(MatchInfo Match, Pick? Pick, IReadOnlyList<string> Steps);

public static class StakeCalculator
{
    /// <summary>Base stake doubled per consecutive loss: 0 → base, 1 → ×2, 2 → ×4, 3 → ×8. A win resets the streak.</summary>
    public static decimal NextStake(int lossStreak, decimal baseStake) => baseStake * (decimal)Math.Pow(2, lossStreak);

    public static decimal NextStake(Domain.Account account) => NextStake(account.LossStreak, account.BaseStake);
}

public static class SlipBuilder
{
    /// <summary>Keeps only matches from the account's registered leagues (case-insensitive, trimmed).</summary>
    public static IEnumerable<MatchInfo> InLeagues(IEnumerable<MatchInfo> matches, IEnumerable<string> leagues)
    {
        var set = new HashSet<string>(leagues.Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
        return matches.Where(m => set.Contains(m.League.Trim()));
    }

    /// <summary>
    /// The one selection we would take from a match. The account's markets registered for the match's league are tried
    /// in order (1st choice, then 2nd, …):
    /// the first market with a line at odds within [MinPickOdds, MaxPickOdds] is used, taking its highest such line
    /// (on the same line the lower odds wins). For Under the highest line in range is also the safest pick.
    /// No usable line in any of the account's markets = no pick from this match.
    /// </summary>
    public static Pick? BestPick(MatchInfo match, BettingRules rules)
    {
        if (ExcludedTeam(match, rules) is not null) return null;
        foreach (var choice in rules.Markets)
        {
            // A market row only applies to the leagues registered on it.
            if (!choice.Leagues.Any(l => l.Trim().Equals(match.League.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
            var sel = match.Selections
                .Where(s => choice.Market.Equals(s.Market, StringComparison.OrdinalIgnoreCase)
                            && choice.Side.Equals(s.Side, StringComparison.OrdinalIgnoreCase)
                            && s.Interval == choice.IntervalKey // whole-match choice only takes whole-match lines, and vice versa
                            && (choice.RequiredLine is not { } line || s.Line == line) // a value set by the user = exactly that line
                            && s.Odds >= rules.MinPickOdds && s.Odds <= rules.MaxPickOdds
                            && TakenBy(match, s, rules) is null) // already bet in these minutes on an open slip
                .OrderByDescending(s => s.Line).ThenBy(s => s.Odds)
                .FirstOrDefault();
            if (sel is not null) return new Pick(match, sel);
        }
        return null;
    }

    /// <summary>
    /// Same decision as <see cref="BestPick"/>, with the reason written out step by step for the analysis report,
    /// e.g. "1st Corners Under: no line at 1.10–1.20 (closest Under 8.5 @ 1.25)" then "2nd Yellow cards Under: ✓ Under 4.5 @ 1.15".
    /// </summary>
    public static MatchDecision Explain(MatchInfo match, BettingRules rules)
    {
        var steps = new List<string>();
        if (ExcludedTeam(match, rules) is { } team)
            return new MatchDecision(match, null, [$"{team} is an excluded team (Settings): no bet on this match."]);
        var rows = rules.Markets.Select((c, i) => (c, i))
            .Where(x => x.c.Leagues.Any(l => l.Trim().Equals(match.League.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (rows.Count == 0)
            return new MatchDecision(match, null, [$"No market row lists {match.League}."]);

        foreach (var (choice, index) in rows)
        {
            var label = $"{Ordinal(index + 1)} {Describe(choice)}";
            var offered = match.Selections
                .Where(s => choice.Market.Equals(s.Market, StringComparison.OrdinalIgnoreCase)
                            && choice.Side.Equals(s.Side, StringComparison.OrdinalIgnoreCase)
                            && s.Interval == choice.IntervalKey
                            && (choice.RequiredLine is not { } line || s.Line == line))
                .ToList();
            if (offered.Count == 0) { steps.Add($"{label}: not offered for this match."); continue; }

            var pick = BestPick(match, rules.WithOnly(choice));
            if (pick is not null)
            {
                steps.Add($"{label}: ✓ {SelectionText(pick.Selection)} @ {pick.Odds:0.00}");
                return new MatchDecision(match, pick, steps);
            }
            var taken = offered.Where(s => s.Odds >= rules.MinPickOdds && s.Odds <= rules.MaxPickOdds)
                .Select(s => (s, t: TakenBy(match, s, rules))).FirstOrDefault(x => x.t is not null);
            if (taken.t is { } t)
            {
                steps.Add($"{label}: {SelectionText(taken.s)} @ {taken.s.Odds:0.00} is in range, but this match is already bet " +
                          $"in the same minutes on {t.Where} (open): skipped.");
                continue;
            }
            var closest = offered.OrderBy(s => Math.Abs(s.Odds - (rules.MinPickOdds + rules.MaxPickOdds) / 2)).First();
            steps.Add($"{label}: no line at {rules.MinPickOdds:0.00}–{rules.MaxPickOdds:0.00} (closest {SelectionText(closest)} @ {closest.Odds:0.00}).");
        }
        steps.Add("No usable market → no bet on this match.");
        return new MatchDecision(match, null, steps);
    }

    /// <summary>
    /// The excluded team (from Settings) playing in this match, or null. Whole words, ignoring case and punctuation:
    /// "Barcelona" also matches "FC Barcelona" and "Barcelona U19".
    /// </summary>
    public static string? ExcludedTeam(MatchInfo match, BettingRules rules)
    {
        static string N(string s) => System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        static bool Has(string team, string excluded) => $" {N(team)} ".Contains($" {N(excluded)} ");
        return rules.ExcludedTeams.FirstOrDefault(x => N(x).Length > 0 && (Has(match.Home, x) || Has(match.Away, x)));
    }

    /// <summary>
    /// Markets that win or lose together count as one: goals Under 0.5 in 1'–10' = "No goal" in 1'–10' = draw after 10'
    /// (all lose on an early goal). Other markets are their own family.
    /// </summary>
    public static string Family(string market) =>
        market.Equals("goals", StringComparison.OrdinalIgnoreCase) || market.Equals("result", StringComparison.OrdinalIgnoreCase)
            ? "goal" : market.ToLowerInvariant();

    /// <summary>"1-10", "1-5", or "match" for a whole-match line. 1'–5' and 1'–10' differ (a goal at 7' wins one, loses the other).</summary>
    public static string Range(string? interval) => interval ?? "match";

    /// <summary>
    /// The open pick that already covers this selection, or null. Same match across sites = kickoff within 15 minutes and the
    /// home or the away team the same (case/punctuation ignored; a name of 5+ letters inside the other also counts,
    /// e.g. "Manchester United FC" = "Manchester United").
    /// </summary>
    public static TakenPick? TakenBy(MatchInfo match, Selection sel, BettingRules rules)
    {
        if (rules.Taken.Count == 0) return null;
        string family = Family(sel.Market), range = Range(sel.Interval);
        return rules.Taken.FirstOrDefault(t => t.Family == family && t.Range == range
            && Math.Abs((t.Kickoff - match.Kickoff).TotalMinutes) <= 15
            && (SameTeam(t.Home, match.Home) || SameTeam(t.Away, match.Away)));
    }

    private static bool SameTeam(string a, string b)
    {
        static string N(string s) => System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
        string x = N(a), y = N(b);
        if (x.Length == 0 || y.Length == 0) return false;
        return x == y || (Math.Min(x.Length, y.Length) >= 5 && (x.Contains(y) || y.Contains(x)));
    }

    public static string SelectionText(Selection s) =>
        s.Label is not null ? s.Label
        : s.Side == "Draw" ? $"draw after minute {s.Interval?.Split('-')[^1]}"
        : s.Interval is null ? $"{s.Side} {s.Line}"
        : s.Line == 0.5m ? $"none in minutes {s.Interval}"
        : $"{s.Side} {s.Line} in minutes {s.Interval}";

    private static string Describe(Domain.MarketChoice c)
    {
        if (c.Market == "result") return $"Result: draw after minute {c.IntervalTo}";
        var name = c.Market switch { "cards" => "Yellow cards", "corners" => "Corners", "goals" => "Goals", "fouls" => "Fouls", _ => c.Market };
        var under = c.Line is { } l ? $"{c.Side} {l}" : c.Side;
        return c.IntervalKey is null ? $"{name} {under}"
             : c.RequiredLine == 0.5m ? $"{name} none in {c.IntervalKey} min"
             : $"{name} {under} in {c.IntervalKey} min";
    }

    private static string Ordinal(int n) => n + (n == 1 ? "st" : n == 2 ? "nd" : n == 3 ? "rd" : "th");

    /// <summary>
    /// Picks at most MaxMatches selections (one per match) with combined odds in
    /// [MinCombinedOdds, MaxCombinedOdds]. Safest slip = lowest combined odds that reaches the
    /// minimum; ties go to fewer matches, then the earliest last kickoff. Null = no bet.
    /// </summary>
    /// <summary>The same match on any page: teams (case, spaces and punctuation ignored) and kickoff.</summary>
    public static string MatchKey(MatchInfo m)
    {
        static string N(string s) => System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
        return $"{N(m.Home)}|{N(m.Away)}|{m.Kickoff:yyyyMMddHHmm}";
    }

    public static SlipPlan? Build(IEnumerable<MatchInfo> matches, BettingRules rules)
    {
        // One pick per match: betting sites refuse two selections of the same match in one accumulator. A match listed
        // twice (two leagues, or two addresses) counts once: same teams at the same kickoff.
        var candidates = matches
            .DistinctBy(m => m.Id)
            .DistinctBy(MatchKey)
            .Select(m => BestPick(m, rules))
            .OfType<Pick>()
            .OrderBy(p => p.Odds).ThenBy(p => p.Match.Kickoff)
            .Take(60) // higher odds than these never make the safest slip
            .ToList();
        if (candidates.Count == 0) return null;

        double min = (double)rules.MinCombinedOdds, max = (double)rules.MaxCombinedOdds;
        double topOdds = (double)candidates[^1].Odds;
        List<Pick>? best = null;
        double bestOdds = double.MaxValue;
        var stack = new List<Pick>();
        int steps = 0;

        bool Better(double p)
        {
            if (best is null) return true;
            if (Math.Abs(p - bestOdds) > 1e-9) return p < bestOdds;
            if (stack.Count != best.Count) return stack.Count < best.Count;
            return stack.Max(x => x.Match.Kickoff) < best.Max(x => x.Match.Kickoff);
        }

        void Dfs(int start, double p)
        {
            if (++steps > 200_000) return;
            if (p >= min - 1e-9)
            {
                if (p <= max + 1e-9 && Better(p)) { best = [.. stack]; bestOdds = p; }
                return; // more picks only raise the odds
            }
            if (stack.Count == rules.MaxMatches || p >= bestOdds) return;
            if (p * Math.Pow(topOdds, rules.MaxMatches - stack.Count) < min) return; // cannot reach the minimum
            for (int i = start; i < candidates.Count; i++)
            {
                stack.Add(candidates[i]);
                Dfs(i + 1, p * (double)candidates[i].Odds);
                stack.RemoveAt(stack.Count - 1);
            }
        }

        Dfs(0, 1);
        if (best is null) return null;
        var combined = best.Aggregate(1m, (acc, x) => acc * x.Odds);
        return new SlipPlan(best.OrderBy(x => x.Match.Kickoff).ToList(), Math.Round(combined, 3));
    }
}
