namespace Mikeka.Api.Services;

/// <summary>Betting rules from systemrequirements.docx (section "Betting" in appsettings.json).</summary>
public class BettingRules
{
    /// <summary>Suggested base stake for a new account; each account sets its own.</summary>
    public decimal BaseStake { get; set; } = 1000;
    public decimal MinCombinedOdds { get; set; } = 2.20m;
    public decimal MaxCombinedOdds { get; set; } = 2.50m;
    public decimal MinPickOdds { get; set; } = 1.10m;
    public decimal MaxPickOdds { get; set; } = 1.20m;
    public int MaxMatches { get; set; } = 6;
    /// <summary>Markets the system supports; each account picks from these (with a side).</summary>
    public string[] SupportedMarkets { get; set; } = ["goals", "corners", "cards", "fouls", "result"];
    /// <summary>Sides accepted on account markets. Under only: the system never bets Over.</summary>
    public static readonly string[] Sides = ["Under"];
    /// <summary>The (market, side) pairs the slip builder may use. Set per account with <see cref="ForAccount"/>.</summary>
    public List<Domain.MarketChoice> Markets { get; set; } = [];
    /// <summary>Losses in a row that stop betting; set per account (Account.MaxLosses) by <see cref="ForAccount"/>.</summary>
    public int StopAfterLosses { get; set; } = 4;
    /// <summary>An alert email goes out one loss before the stop.</summary>
    public int AlertAfterLosses => StopAfterLosses - 1;
    /// <summary>Teams never bet on (Settings page, all accounts).</summary>
    public List<string> ExcludedTeams { get; set; } = [];
    // Timing (Settings page, copied here by SettingsService).
    /// <summary>Matches are looked for from now over up to this many consecutive days (today first).</summary>
    public int MaxDaysAhead { get; set; } = 7;
    /// <summary>Each account is checked again this long after its last check.</summary>
    public int CheckIntervalMinutes { get; set; } = 60;
    /// <summary>On one betting company, the next account is checked this long after the previous one finished.</summary>
    public int SameSiteDelayMinutes { get; set; } = 5;
    /// <summary>A match is assumed over this long after kickoff.</summary>
    public int MatchMinutes { get; set; } = 180;
    /// <summary>Two match ends further apart than this get separate result checks.</summary>
    public int SettlementGapMinutes { get; set; } = 60;
    /// <summary>"Coldbet" for the real site, "Mock" for testing without the site.</summary>
    public string Bookmaker { get; set; } = "Mock";
    /// <summary>When true, builds and logs slips but never clicks "place bet".</summary>
    public bool DryRun { get; set; } = true;
    /// <summary>
    /// With DryRun off: false (default) = click the picks' odds into the site's bet slip and stop, the user clicks "Place";
    /// true = the system clicks "Place" itself (Coldbet only).
    /// </summary>
    public bool PlaceBets { get; set; } = false;

    /// <summary>
    /// When to read a slip's result (UTC). Each match ends at kickoff + MatchMinutes. Going through the ends in order, an end
    /// becomes a check time when the next match ends more than SettlementGapMinutes later; the last end always is one.
    /// E.g. ends 17:00, 17:30, 20:00 with a 60-minute gap → checks at 17:30 and 20:00.
    /// </summary>
    public List<DateTime> SettlementChecks(IEnumerable<DateTime> kickoffsUtc)
    {
        var ends = kickoffsUtc.Select(k => k.AddMinutes(MatchMinutes)).Order().ToList();
        var checks = new List<DateTime>();
        for (int i = 0; i < ends.Count; i++)
            if (i == ends.Count - 1 || ends[i + 1] - ends[i] > TimeSpan.FromMinutes(SettlementGapMinutes))
                checks.Add(ends[i]);
        return checks;
    }

    /// <summary>A copy of these rules limited to one market row (used to explain decisions).</summary>
    public BettingRules WithOnly(Domain.MarketChoice choice)
    {
        var copy = (BettingRules)MemberwiseClone();
        copy.Markets = [choice];
        return copy;
    }

    /// <summary>Checks a set of odds ranges; returns the problem in plain words, or null if they are usable.</summary>
    public static string? OddsError(decimal minPick, decimal maxPick, decimal minCombined, decimal maxCombined, int maxMatches)
    {
        if (minPick < 1.01m) return "Minimum pick odds must be at least 1.01.";
        if (maxPick <= minPick) return "Maximum pick odds must be higher than the minimum.";
        if (maxCombined <= minCombined) return "Maximum combined odds must be higher than the minimum.";
        if (minCombined < minPick) return "Minimum combined odds can't be below the minimum pick odds.";
        var best = (decimal)Math.Pow((double)maxPick, maxMatches);
        if (best < minCombined)
            return $"With picks up to {maxPick:0.00} and at most {maxMatches} matches, the best slip is {best:0.00}, " +
                   $"so {minCombined:0.00} can never be reached.";
        return null;
    }

    /// <summary>A copy of these rules using the markets and sides the account chose, its odds ranges and maximum losses.</summary>
    public BettingRules ForAccount(Domain.Account account)
    {
        var copy = (BettingRules)MemberwiseClone();
        copy.Markets = account.Markets.Where(m => SupportedMarkets.Contains(m.Market, StringComparer.OrdinalIgnoreCase)).ToList();
        (copy.MinPickOdds, copy.MaxPickOdds) = (account.MinPickOdds, account.MaxPickOdds);
        (copy.MinCombinedOdds, copy.MaxCombinedOdds) = (account.MinCombinedOdds, account.MaxCombinedOdds);
        copy.StopAfterLosses = account.MaxLosses;
        copy.MaxMatches = account.MaxPicks;
        return copy;
    }
}

public class EmailOptions
{
    public string To { get; set; } = "batumunwa@gmail.com";
    public string? From { get; set; }
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 465;
    public string? User { get; set; }
    public string? Password { get; set; }
}

public static class Eat
{
    public static readonly TimeZoneInfo Zone = Find();

    private static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "Africa/Dar_es_Salaam", "E. Africa Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.CreateCustomTimeZone("EAT", TimeSpan.FromHours(3), "EAT", "EAT");
    }

    public static DateTime Now(TimeProvider clock) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime;
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Now(clock));
}
