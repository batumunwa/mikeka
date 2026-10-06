using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Tests;

public class SettingsTests
{
    private static (SettingsService service, BettingRules rules) Create()
    {
        var db = new MikekaDb(new DbContextOptionsBuilder<MikekaDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var rules = new BettingRules();
        return (new SettingsService(db, Options.Create(rules)), rules);
    }

    private static BettingSettings S(decimal minPick, decimal maxPick, decimal minComb, decimal maxComb) =>
        new() { MinPickOdds = minPick, MaxPickOdds = maxPick, MinCombinedOdds = minComb, MaxCombinedOdds = maxComb };

    [Fact]
    public void Accepts_the_requested_ranges() => Assert.Null(Create().service.Validate(S(1.10m, 1.20m, 2.10m, 2.20m)));

    [Theory]
    [InlineData(1.20, 1.10, 2.10, 2.20, "higher than the minimum")]
    [InlineData(1.10, 1.20, 2.20, 2.10, "higher than the minimum")]
    [InlineData(1.00, 1.20, 2.10, 2.20, "at least 1.01")]
    [InlineData(1.05, 1.10, 2.10, 2.20, "can never be reached")] // 1.10^6 = 1.77
    public void Rejects_unusable_ranges(double minPick, double maxPick, double minComb, double maxComb, string problem) =>
        Assert.Contains(problem, Create().service.Validate(S((decimal)minPick, (decimal)maxPick, (decimal)minComb, (decimal)maxComb)));

    [Fact]
    public async Task Saving_updates_the_live_rules_used_by_the_engine()
    {
        var (service, rules) = Create();
        await service.SaveAsync(S(1.12m, 1.18m, 2.15m, 2.30m), default);
        Assert.Equal((1.12m, 1.18m, 2.15m, 2.30m), (rules.MinPickOdds, rules.MaxPickOdds, rules.MinCombinedOdds, rules.MaxCombinedOdds));
        Assert.Equal(2.15m, (await service.GetAsync(default)).MinCombinedOdds);
    }
}
