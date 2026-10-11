namespace Mikeka.Api.Domain;

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Which betting site this account is on: "coldbet" or "1win". Picks the site client used for it.</summary>
    public string Site { get; set; } = "coldbet";
    public string Username { get; set; } = "";
    /// <summary>Encrypted with ASP.NET Data Protection; never returned by the API.</summary>
    public string PasswordProtected { get; set; } = "";
    public string Currency { get; set; } = "TZS";
    public bool IsActive { get; set; } = true;
    /// <summary>All leagues this account looks at: kept equal to the union of its markets' leagues (set on save).</summary>
    public List<string> Leagues { get; set; } = new();
    /// <summary>Markets this account bets on (goals/corners/cards/fouls), each with its side (always Under). At least one.</summary>
    public List<MarketChoice> Markets { get; set; } = new();

    /// <summary>Normal stake for this account; doubled after each consecutive loss, back to this after a win.</summary>
    public decimal BaseStake { get; set; } = 1000;

    /// <summary>Most picks (matches) one slip may hold for this account (new accounts: Betting:MaxMatches, 6).</summary>
    public int MaxPicks { get; set; } = 6;

    /// <summary>Betting stops after this many losses in a row (default from Settings when the account is created).</summary>
    public int MaxLosses { get; set; } = 4;

    /// <summary>This account's odds ranges (defaults from Settings when the account is created).</summary>
    public decimal MinPickOdds { get; set; } = 1.10m;
    public decimal MaxPickOdds { get; set; } = 1.20m;
    public decimal MinCombinedOdds { get; set; } = 2.10m;
    public decimal MaxCombinedOdds { get; set; } = 2.20m;

    /// <summary>Consecutive lost slips; drives the stake (base, ×2, ×4, ×8).</summary>
    public int LossStreak { get; set; }
    /// <summary>Set after MaxLosses losses in a row. Betting resumes only after a manual reset.</summary>
    public bool Stopped { get; set; }
    /// <summary>Stopped by hand ("Stop betting"): no new slips; open slips' results are still read. "Resume betting" clears it.</summary>
    public bool BettingPaused { get; set; }
    public decimal? LastBalance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>When the scheduler next checks this account (matches, or the open slip's result). Null = as soon as possible.</summary>
    public DateTime? NextCheckAt { get; set; }

    public List<Slip> Slips { get; set; } = new();
}

/// <summary>One market an account bets on, e.g. total corners Under. Stored as JSON on the account.</summary>
public class MarketChoice
{
    /// <summary>"goals", "corners", "cards" (yellow cards), "fouls", or "result" (the score is a draw at the end of the interval).</summary>
    public string Market { get; set; } = "";
    /// <summary>"Under" for totals; "Draw" for the "result" market.</summary>
    public string Side { get; set; } = "Under";
    /// <summary>Leagues where this market is used (as named on the site). At least one.</summary>
    public List<string> Leagues { get; set; } = new();
    /// <summary>Optional time interval in minutes (e.g. 1–10). When set, the bet is "No" (nothing happens) in that interval.</summary>
    public int? IntervalFrom { get; set; }
    public int? IntervalTo { get; set; }

    /// <summary>
    /// Optional exact line, e.g. 0.5 for "Under 0.5". Empty: whole match = highest line with odds in range;
    /// interval = 0.5 ("none in the interval").
    /// </summary>
    public decimal? Line { get; set; }

    /// <summary>The exact line this choice requires, if any (an interval without a value means Under 0.5).</summary>
    public decimal? RequiredLine => Market == "result" ? null : Line ?? (IntervalKey is not null ? 0.5m : null);

    /// <summary>"1-10" for an interval choice, null for a whole-match Under.</summary>
    public string? IntervalKey => IntervalFrom is { } f && IntervalTo is { } t ? $"{f}-{t}" : null;
}

/// <summary>Draft = prepared in this system from an analysis, not placed with the betting company yet.</summary>
public enum SlipStatus { Pending, Won, Lost, Skipped, Draft }

public class Slip
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>The EAT calendar day this slip was generated for.</summary>
    public DateOnly BetDay { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SettledAt { get; set; }

    public SlipStatus Status { get; set; }
    public decimal Stake { get; set; }
    public decimal CombinedOdds { get; set; }
    public decimal PotentialReturn { get; set; }
    /// <summary>Bet/coupon number from the bookmaker, used to check the result.</summary>
    public string? BetReference { get; set; }
    public decimal? BalanceBefore { get; set; }
    public decimal? BalanceAfter { get; set; }
    /// <summary>Why a day was skipped, or other notes.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// UTC times to read the slip's result: each match ends at kickoff + match length; an end is a check time when the next
    /// match ends more than the settlement gap later, and the last end always is. A loss seen early lets the next slip start.
    /// </summary>
    public List<DateTime> SettlementChecks { get; set; } = new();
    /// <summary>When the result was last read from the site (UTC).</summary>
    public DateTime? ResultCheckedAt { get; set; }

    public List<SlipPick> Picks { get; set; } = new();
}

public class SlipPick
{
    public int Id { get; set; }
    public int SlipId { get; set; }
    public string MatchId { get; set; } = "";
    public string League { get; set; } = "";
    public string Home { get; set; } = "";
    public string Away { get; set; } = "";
    public DateTime Kickoff { get; set; }
    /// <summary>"cards" or "corners".</summary>
    public string Market { get; set; } = "";
    public string Side { get; set; } = "Over";
    public decimal Line { get; set; }
    /// <summary>"1-10" when the pick is "no event in minutes 1–10"; null for a whole-match total.</summary>
    public string? Interval { get; set; }
    /// <summary>The site's own market and outcome when it differs from the usual wording, e.g. "10 Minute Result: X".</summary>
    public string? Label { get; set; }
    public decimal Odds { get; set; }
    /// <summary>This pick's own result: null = not known yet. A won slip marks all its picks Won; on a lost slip the user
    /// marks which pick(s) lost (Slips table), so the statistics show which leagues and markets lose.</summary>
    public PickResult? Result { get; set; }
}

public enum PickResult { Won, Lost }

/// <summary>System-wide betting settings edited on the Settings page (a single row, Id = 1).</summary>
public class BettingSettings
{
    public int Id { get; set; } = 1;
    /// <summary>Each pick's odds must be within this range.</summary>
    public decimal MinPickOdds { get; set; } = 1.10m;
    public decimal MaxPickOdds { get; set; } = 1.20m;
    /// <summary>The slip's combined odds must be within this range.</summary>
    public decimal MinCombinedOdds { get; set; } = 2.10m;
    public decimal MaxCombinedOdds { get; set; } = 2.20m;
    /// <summary>Default maximum losses in a row for new accounts.</summary>
    public int MaxLosses { get; set; } = 4;
    /// <summary>Matches with any of these teams are never picked (all accounts). Matched ignoring case, spaces and punctuation.</summary>
    public List<string> ExcludedTeams { get; set; } = new();
    /// <summary>Each account is checked again this long after its last check.</summary>
    public int CheckIntervalMinutes { get; set; } = 60;
    /// <summary>On one betting company, the next account is checked this long after the previous one finished.</summary>
    public int SameSiteDelayMinutes { get; set; } = 5;
    /// <summary>A match is assumed over this long after kickoff.</summary>
    public int MatchMinutes { get; set; } = 180;
    /// <summary>Two match ends further apart than this get separate result checks.</summary>
    public int SettlementGapMinutes { get; set; } = 60;
    /// <summary>Matches are looked for from now over up to this many consecutive days.</summary>
    public int MaxDaysAhead { get; set; } = 7;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>An account's balance at a moment: written each time the stored balance changes (runs, checks, settled slips).</summary>
public class BalanceEntry
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public decimal Balance { get; set; }
}

public class RunLog
{
    public long Id { get; set; }
    public int? AccountId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = "";
}
