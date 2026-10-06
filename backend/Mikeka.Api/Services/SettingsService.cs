using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;

namespace Mikeka.Api.Services;

/// <summary>
/// Odds settings live in the database (Settings page). They are copied onto the shared <see cref="BettingRules"/>
/// instance at startup and on every save, so the engine, analysis and site clients all use the current values.
/// </summary>
public class SettingsService(MikekaDb db, IOptions<BettingRules> rules)
{
    public async Task<BettingSettings> GetAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, ct) ?? new BettingSettings();

    /// <summary>Copies the stored settings onto the live rules. Called once at startup.</summary>
    public async Task ApplyAsync(CancellationToken ct) => Apply(await GetAsync(ct));

    /// <summary>Checks a new set of values; returns the problem in plain words, or null if they are usable.</summary>
    public string? Validate(BettingSettings s)
    {
        if (s.MinPickOdds < 1.01m) return "Minimum pick odds must be at least 1.01.";
        if (s.MaxPickOdds <= s.MinPickOdds) return "Maximum pick odds must be higher than the minimum.";
        if (s.MaxCombinedOdds <= s.MinCombinedOdds) return "Maximum combined odds must be higher than the minimum.";
        if (s.MinCombinedOdds < s.MinPickOdds) return "Minimum combined odds can't be below the minimum pick odds.";
        var maxMatches = rules.Value.MaxMatches;
        var best = (decimal)Math.Pow((double)s.MaxPickOdds, maxMatches);
        if (best < s.MinCombinedOdds)
            return $"With picks up to {s.MaxPickOdds:0.00} and at most {maxMatches} matches, the best slip is {best:0.00}, " +
                   $"so {s.MinCombinedOdds:0.00} can never be reached.";
        return null;
    }

    public async Task<BettingSettings> SaveAsync(BettingSettings s, CancellationToken ct)
    {
        var row = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (row is null) { row = new BettingSettings(); db.Settings.Add(row); }
        row.MinPickOdds = s.MinPickOdds;
        row.MaxPickOdds = s.MaxPickOdds;
        row.MinCombinedOdds = s.MinCombinedOdds;
        row.MaxCombinedOdds = s.MaxCombinedOdds;
        row.UpdatedAt = DateTime.UtcNow;
        db.RunLogs.Add(new RunLog
        {
            Message = $"Settings changed: pick odds {row.MinPickOdds:0.00}–{row.MaxPickOdds:0.00}, combined {row.MinCombinedOdds:0.00}–{row.MaxCombinedOdds:0.00}.",
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
    }
}
