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
    public int AlertAfterLosses { get; set; } = 3;
    public int StopAfterLosses { get; set; } = 4;
    /// <summary>Spread matches over up to this many days when today cannot reach the odds.</summary>
    public int MaxDaysAhead { get; set; } = 3;
    /// <summary>Daily generation time in East Africa Time.</summary>
    public TimeOnly RunAt { get; set; } = new(8, 0);
    /// <summary>A betting window runs 24 hours from this time (EAT), e.g. 10:00 today to 09:59 tomorrow.</summary>
    public TimeOnly WindowStart { get; set; } = new(10, 0);
    public int TickMinutes { get; set; } = 30;
    /// <summary>"Coldbet" for the real site, "Mock" for testing without the site.</summary>
    public string Bookmaker { get; set; } = "Mock";
    /// <summary>When true, builds and logs slips but never clicks "place bet".</summary>
    public bool DryRun { get; set; } = true;
    /// <summary>
    /// With DryRun off: false (default) = click the picks' odds into the site's bet slip and stop, the user clicks "Place";
    /// true = the system clicks "Place" itself (Coldbet only).
    /// </summary>
    public bool PlaceBets { get; set; } = false;

    /// <summary>A copy of these rules limited to one market row (used to explain decisions).</summary>
    public BettingRules WithOnly(Domain.MarketChoice choice)
    {
        var copy = (BettingRules)MemberwiseClone();
        copy.Markets = [choice];
        return copy;
    }

    /// <summary>A copy of these rules using the markets and sides the account chose.</summary>
    public BettingRules ForAccount(Domain.Account account)
    {
        var copy = (BettingRules)MemberwiseClone();
        copy.Markets = account.Markets.Where(m => SupportedMarkets.Contains(m.Market, StringComparer.OrdinalIgnoreCase)).ToList();
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
