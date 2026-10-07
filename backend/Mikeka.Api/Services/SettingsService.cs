using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;

namespace Mikeka.Api.Services;

/// <summary>
/// Settings (odds defaults, maximum losses, excluded teams) live in the database (Settings page). They are copied onto the shared <see cref="BettingRules"/>
/// instance at startup and on every save, so the engine, analysis and site clients all use the current values.
/// </summary>
public class SettingsService(MikekaDb db, IOptions<BettingRules> rules)
{
    public async Task<BettingSettings> GetAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, ct) ?? new BettingSettings();

    /// <summary>Copies the stored settings onto the live rules. Called once at startup.</summary>
    public async Task ApplyAsync(CancellationToken ct) => Apply(await GetAsync(ct));

    /// <summary>Checks a new set of values; returns the problem in plain words, or null if they are usable.</summary>
    public string? Validate(BettingSettings s) =>
        s.MaxLosses < 1 ? "Maximum losses must be at least 1."
        : s.CheckIntervalMinutes is < 5 or > 1440 ? "Check every: between 5 and 1440 minutes."
        : s.SameSiteDelayMinutes is < 0 or > 240 ? "Delay between accounts of one company: between 0 and 240 minutes."
        : s.MatchMinutes is < 90 or > 360 ? "Match length: between 90 and 360 minutes."
        : s.SettlementGapMinutes is < 0 or > 1440 ? "Settlement gap: between 0 and 1440 minutes."
        : s.MaxDaysAhead is < 1 or > 30 ? "Days to look ahead: between 1 and 30."
        : BettingRules.OddsError(s.MinPickOdds, s.MaxPickOdds, s.MinCombinedOdds, s.MaxCombinedOdds, rules.Value.MaxMatches);

    /// <summary>One entry per team, trimmed, no duplicates.</summary>
    public static List<string> CleanTeams(IEnumerable<string>? teams) =>
        (teams ?? []).SelectMany(t => t.Split(',', ';', '\n')).Select(t => t.Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<BettingSettings> SaveAsync(BettingSettings s, CancellationToken ct)
    {
        var row = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (row is null) { row = new BettingSettings(); db.Settings.Add(row); }
        row.MinPickOdds = s.MinPickOdds;
        row.MaxPickOdds = s.MaxPickOdds;
        row.MinCombinedOdds = s.MinCombinedOdds;
        row.MaxCombinedOdds = s.MaxCombinedOdds;
        row.MaxLosses = s.MaxLosses;
        row.ExcludedTeams = CleanTeams(s.ExcludedTeams);
        row.CheckIntervalMinutes = s.CheckIntervalMinutes;
        row.SameSiteDelayMinutes = s.SameSiteDelayMinutes;
        row.MatchMinutes = s.MatchMinutes;
        row.SettlementGapMinutes = s.SettlementGapMinutes;
        row.MaxDaysAhead = s.MaxDaysAhead;
        row.UpdatedAt = DateTime.UtcNow;
        db.RunLogs.Add(new RunLog
        {
            Message = $"Settings changed: pick odds {row.MinPickOdds:0.00}–{row.MaxPickOdds:0.00}, combined {row.MinCombinedOdds:0.00}–{row.MaxCombinedOdds:0.00}, " +
                      $"max losses {row.MaxLosses}, excluded teams: {(row.ExcludedTeams.Count == 0 ? "none" : string.Join(", ", row.ExcludedTeams))}, " +
                      $"check every {row.CheckIntervalMinutes} min, {row.SameSiteDelayMinutes} min between accounts of one company, " +
                      $"match {row.MatchMinutes} min, settlement gap {row.SettlementGapMinutes} min, {row.MaxDaysAhead} days ahead.",
        });
        await db.SaveChangesAsync(ct);
        Apply(row);
        return row;
    }

    private void Apply(BettingSettings s)
    {
        var r = rules.Value; // the single shared instance every service reads
        r.MinPickOdds = s.MinPickOdds;
        r.MaxPickOdds = s.MaxPickOdds;
        r.MinCombinedOdds = s.MinCombinedOdds;
        r.MaxCombinedOdds = s.MaxCombinedOdds;
        r.StopAfterLosses = s.MaxLosses;
        r.ExcludedTeams = s.ExcludedTeams;
        r.CheckIntervalMinutes = s.CheckIntervalMinutes;
        r.SameSiteDelayMinutes = s.SameSiteDelayMinutes;
        r.MatchMinutes = s.MatchMinutes;
        r.SettlementGapMinutes = s.SettlementGapMinutes;
        r.MaxDaysAhead = s.MaxDaysAhead;
    }
}
