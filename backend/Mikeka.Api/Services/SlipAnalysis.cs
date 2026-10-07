using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Mikeka.Api.Bookmaker;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;

namespace Mikeka.Api.Services;

public record AnalysedMatch(
    string League, string Home, string Away, DateTime Kickoff, string Url,
    IReadOnlyList<string> Steps, string? Pick, decimal? PickOdds, bool InSlip);

/// <param name="LastKnownBalance">From the last logged-in check; the analysis itself reads the public site without logging in.</param>
public record AnalysisReport(
    DateTime FromEat, DateTime ToEat, decimal? LastKnownBalance, int LossStreak, decimal Stake, string Currency,
    IReadOnlyList<AnalysedMatch> Matches,
    decimal? CombinedOdds, decimal? PotentialReturn, decimal? ChancePercent,
    bool CanCreateSlip, string Verdict);

/// <summary>
/// Analysis of a betting window with the account's settings, read from the bookmaker's public pages (no login):
/// which matches are found, what each one would get and why, and the slip the system would build.
/// The proposed slip can then be saved in this system as a Draft. Never places a bet.
/// </summary>
public class SlipAnalysis(
    MikekaDb db, IBookmakerFactory bookmakers, AccountSecrets secrets, ExcelLog excel, ScreenshotOddsReader screenshotReader,
    IOptions<BettingRules> rulesOptions, TimeProvider clock, ILogger<SlipAnalysis> log)
{
    private readonly BettingRules _rules = rulesOptions.Value;

    /// <summary>The last proposed slip per account, kept so "Create slip" saves exactly what was shown.</summary>
    private static readonly ConcurrentDictionary<int, (SlipPlan plan, DateTime fromEat, DateTime toEat, decimal stake)> LastPlans = new();

    /// <summary>Default window: from now (EAT) over the days the automatic checks look ahead (Settings).</summary>
    public (DateTime fromEat, DateTime toEat) DefaultWindow()
    {
        var now = Eat.Now(clock);
        var start = now.Date.AddHours(now.Hour).AddMinutes(now.Minute);
        return (start, now.Date.AddDays(_rules.MaxDaysAhead).AddMinutes(-1));
    }

    public async Task<AnalysisReport> AnalyseAsync(int accountId, DateTime fromEat, DateTime toEat, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([accountId], ct) ?? throw new KeyNotFoundException("Account not found.");
        var rules = _rules.ForAccount(account);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fromEat, DateTimeKind.Unspecified), Eat.Zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(toEat, DateTimeKind.Unspecified), Eat.Zone);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var stake = StakeCalculator.NextStake(account);

        // Public pages only: no login, so no balance here (it is checked when the slip is placed).
        await using var client = await bookmakers.CreateAsync(account, secrets.Unprotect(account.PasswordProtected), ct);
        var all = await client.GetMatchesAsync(account.Leagues, account.Markets, toUtc, ct);

        // Inside the window and not started yet (a started match can't be bet pre-match).
        var matches = SlipBuilder.InLeagues(all, account.Leagues)
            .Where(m => m.Kickoff >= fromUtc && m.Kickoff <= toUtc && m.Kickoff > nowUtc)
            .OrderBy(m => m.Kickoff)
            .ToList();
        var decisions = matches.Select(m => SlipBuilder.Explain(m, rules)).ToList();
        var plan = SlipBuilder.Build(matches, rules);
        var inSlip = plan?.Picks.Select(p => p.Match.Id).ToHashSet() ?? [];

        string verdict;
        var usable = decisions.Count(d => d.Pick is not null);
        if (matches.Count == 0) verdict = "No matches in the window for the account's leagues: no slip.";
        else if (plan is null)
            verdict = $"{usable} of {matches.Count} matches have a usable pick, but no combination of up to {_rules.MaxMatches} reaches " +
                      $"{rules.MinCombinedOdds:0.00}–{rules.MaxCombinedOdds:0.00}: no slip.";
        else verdict = $"Slip ready: {plan.Picks.Count} picks at {plan.CombinedOdds:0.00}, stake {stake:N0} {account.Currency}.";

        if (plan is null) LastPlans.TryRemove(account.Id, out _);
        else LastPlans[account.Id] = (plan, fromEat, toEat, stake);

        // The bookmaker's own odds as a rough chance: 1/odds per pick, multiplied for the slip (margin makes it slightly optimistic).
        decimal? chance = plan is null ? null : Math.Round(100m / plan.CombinedOdds, 1);

        db.RunLogs.Add(new RunLog
        {
            AccountId = account.Id, At = nowUtc,
            Message = $"Analysis {fromEat:dd/MM HH:mm}–{toEat:dd/MM HH:mm} EAT: {matches.Count} matches, {usable} usable. {verdict}",
        });
        await db.SaveChangesAsync(ct);
        log.LogInformation("[{User}] analysis: {Verdict}", account.Username, verdict);

        return new AnalysisReport(
            fromEat, toEat, account.LastBalance, account.LossStreak, stake, account.Currency,
            decisions.Select(d => new AnalysedMatch(
                d.Match.League, d.Match.Home, d.Match.Away, d.Match.Kickoff, d.Match.Id, d.Steps,
                d.Pick is null ? null : $"{MarketName(d.Pick.Selection.Market)} {SlipBuilder.SelectionText(d.Pick.Selection)}",
                d.Pick?.Odds, inSlip.Contains(d.Match.Id))).ToList(),
            plan?.CombinedOdds, plan is null ? null : Math.Round(stake * plan.CombinedOdds, 2), chance,
            plan is not null, verdict);
    }

    /// <summary>
    /// Saves the slip from this account's last analysis as a Draft in our system (not placed with the betting company).
    /// Refused if there is no fresh analysis or a pick has already kicked off.
    /// </summary>
    public async Task<Slip> CreateDraftAsync(int accountId, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([accountId], ct) ?? throw new KeyNotFoundException("Account not found.");
        if (!LastPlans.TryGetValue(accountId, out var last))
            throw new InvalidOperationException("Run the analysis first; there is no proposed slip to save.");
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        if (last.plan.Picks.Any(p => p.Match.Kickoff <= nowUtc))
            throw new InvalidOperationException("A match in the proposed slip has already started. Run the analysis again.");

        var slip = new Slip
        {
            AccountId = account.Id,
            BetDay = DateOnly.FromDateTime(last.fromEat),
            Status = SlipStatus.Draft,
            Stake = last.stake,
            CombinedOdds = last.plan.CombinedOdds,
            PotentialReturn = Math.Round(last.stake * last.plan.CombinedOdds, 2),
            BalanceBefore = account.LastBalance,
            Note = $"Draft from analysis {last.fromEat:dd/MM HH:mm}–{last.toEat:dd/MM HH:mm} EAT; not placed with {account.Site} yet.",
            Picks = last.plan.Picks.Select(p => new SlipPick
            {
                MatchId = p.Match.Id, League = p.Match.League, Home = p.Match.Home, Away = p.Match.Away,
                Kickoff = p.Match.Kickoff, Market = p.Selection.Market, Side = p.Selection.Side,
                Line = p.Selection.Line, Odds = p.Selection.Odds, Interval = p.Selection.Interval, Label = p.Selection.Label,
            }).ToList(),
        };
        db.Slips.Add(slip);
        db.RunLogs.Add(new RunLog
        {
            AccountId = account.Id, At = nowUtc,
            Message = $"Draft slip created: {slip.Picks.Count} picks at {slip.CombinedOdds:0.00}, stake {slip.Stake:N0} {account.Currency}.",
        });
        await db.SaveChangesAsync(ct);
        LastPlans.TryRemove(accountId, out _); // one draft per analysis
        await excel.WriteFileAsync(ct);
        return slip;
    }

    private static string MarketName(string m) => m switch
    {
        "cards" => "Yellow cards", "corners" => "Corners", "goals" => "Goals", "fouls" => "Fouls", _ => m,
    };
}
