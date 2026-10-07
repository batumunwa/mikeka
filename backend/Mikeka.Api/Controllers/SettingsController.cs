using Microsoft.AspNetCore.Mvc;
using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Api.Controllers;

/// <param name="DryRun">True: "Run now" builds slips but never places them (Betting:DryRun in appsettings).</param>
/// <param name="PlaceBets">True: runs click Place themselves; false: they fill the slip and the user places it (Betting:PlaceBets).</param>
public record SettingsDto(decimal MinPickOdds, decimal MaxPickOdds, decimal MinCombinedOdds, decimal MaxCombinedOdds, DateTime UpdatedAt, int MaxMatches, bool DryRun, bool PlaceBets,
    int MaxLosses, List<string> ExcludedTeams,
    int CheckIntervalMinutes, int SameSiteDelayMinutes, int MatchMinutes, int SettlementGapMinutes, int MaxDaysAhead);

public record SaveSettingsRequest(decimal MinPickOdds, decimal MaxPickOdds, decimal MinCombinedOdds, decimal MaxCombinedOdds,
    int MaxLosses = 4, List<string>? ExcludedTeams = null,
    int CheckIntervalMinutes = 60, int SameSiteDelayMinutes = 5, int MatchMinutes = 180, int SettlementGapMinutes = 60, int MaxDaysAhead = 7);

/// <summary>System-wide settings: default odds and maximum losses for new accounts, excluded teams (Settings page), stored in the database.</summary>
[ApiController, Route("api/settings")]
public class SettingsController(SettingsService settings, Microsoft.Extensions.Options.IOptions<BettingRules> rules) : ControllerBase
{
    private SettingsDto ToDto(BettingSettings s) =>
        new(s.MinPickOdds, s.MaxPickOdds, s.MinCombinedOdds, s.MaxCombinedOdds, s.UpdatedAt, rules.Value.MaxMatches, rules.Value.DryRun, rules.Value.PlaceBets,
            s.MaxLosses, s.ExcludedTeams,
            s.CheckIntervalMinutes, s.SameSiteDelayMinutes, s.MatchMinutes, s.SettlementGapMinutes, s.MaxDaysAhead);

    [HttpGet]
    public async Task<SettingsDto> Get(CancellationToken ct) => ToDto(await settings.GetAsync(ct));

    [HttpPut]
    public async Task<ActionResult<SettingsDto>> Save(SaveSettingsRequest req, CancellationToken ct)
    {
        var s = new BettingSettings
        {
            MinPickOdds = req.MinPickOdds, MaxPickOdds = req.MaxPickOdds,
            MinCombinedOdds = req.MinCombinedOdds, MaxCombinedOdds = req.MaxCombinedOdds,
            MaxLosses = req.MaxLosses, ExcludedTeams = SettingsService.CleanTeams(req.ExcludedTeams),
            CheckIntervalMinutes = req.CheckIntervalMinutes, SameSiteDelayMinutes = req.SameSiteDelayMinutes,
            MatchMinutes = req.MatchMinutes, SettlementGapMinutes = req.SettlementGapMinutes, MaxDaysAhead = req.MaxDaysAhead,
        };
        if (settings.Validate(s) is { } problem) return ValidationProblem(problem);
        return ToDto(await settings.SaveAsync(s, ct));
    }
}
