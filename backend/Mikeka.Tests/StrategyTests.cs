using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Tests;

public class StrategyTests
{
    internal static List<MarketChoice> UnderCornersAndCards() => [new() { Market = "corners", Side = "Under", Leagues = ["League A"] }, new() { Market = "cards", Side = "Under", Leagues = ["League A"] }];
    private static readonly BettingRules Rules = new() { Markets = UnderCornersAndCards() };
    private static readonly DateTime T0 = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    internal static MatchInfo Match(int i, params (string market, decimal line, decimal odds)[] sels) =>
        new($"m{i}", "League A", $"H{i}", $"A{i}", T0.AddHours(i),
            sels.Select(s => new Selection(s.market, "Under", s.line, s.odds, $"r{i}-{s.market}-{s.line}")).ToList());

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1, 2000)]
    [InlineData(2, 4000)]
    [InlineData(3, 8000)]
    public void Stake_doubles_per_consecutive_loss(int streak, decimal expected) =>
        Assert.Equal(expected, StakeCalculator.NextStake(streak, 1000m));

    [Fact]
    public void BestPick_takes_highest_line_within_odds_range_only_cards_or_corners()
    {
        var m = Match(1,
            ("corners", 6.5m, 1.05m),  // odds too low
            ("corners", 7.5m, 1.12m),
            ("corners", 8.5m, 1.19m),  // highest line in range -> chosen
            ("corners", 9.5m, 1.35m),  // odds too high
            ("goals", 10.5m, 1.15m));  // wrong market
        var pick = SlipBuilder.BestPick(m, Rules)!;
        Assert.Equal(8.5m, pick.Selection.Line);
        Assert.Equal("corners", pick.Selection.Market);
    }

    [Fact]
    public void BestPick_never_takes_Over()
    {
        var m = new MatchInfo("m1", "League A", "H", "A", T0,
            [new Selection("corners", "Over", 9.5m, 1.15m, "r1"), new Selection("corners", "Under", 7.5m, 1.18m, "r2")]);
        var pick = SlipBuilder.BestPick(m, Rules)!;
        Assert.Equal("Under", pick.Selection.Side);
        Assert.Equal(7.5m, pick.Selection.Line);
    }

    [Fact]
    public void BestPick_uses_first_market_in_order_even_if_a_later_one_has_a_higher_line()
    {
        var rules = new BettingRules { Markets = [new() { Market = "cards", Side = "Under", Leagues = ["League A"] }, new() { Market = "corners", Side = "Under", Leagues = ["League A"] }] };
        var m = Match(1, ("cards", 5.5m, 1.15m), ("corners", 11.5m, 1.12m));
        var pick = SlipBuilder.BestPick(m, rules)!;
        Assert.Equal("cards", pick.Selection.Market);
        Assert.Equal(5.5m, pick.Selection.Line);
    }

    [Fact]
    public void BestPick_falls_through_to_next_market_when_first_is_missing_or_out_of_range()
    {
        var rules = new BettingRules { Markets = [new() { Market = "corners", Side = "Under", Leagues = ["League A"] }, new() { Market = "cards", Side = "Under", Leagues = ["League A"] }] };
        var noCorners = Match(1, ("cards", 5.5m, 1.15m));
        Assert.Equal("cards", SlipBuilder.BestPick(noCorners, rules)!.Selection.Market);
        var cornersOutOfRange = Match(2, ("corners", 11.5m, 1.30m), ("cards", 4.5m, 1.18m));
        Assert.Equal("cards", SlipBuilder.BestPick(cornersOutOfRange, rules)!.Selection.Market);
    }

    [Fact]
    public void BestPick_null_when_no_market_in_order_has_a_usable_line()
    {
        var rules = new BettingRules { Markets = [new() { Market = "corners", Side = "Under", Leagues = ["League A"] }, new() { Market = "cards", Side = "Under", Leagues = ["League A"] }] };
        Assert.Null(SlipBuilder.BestPick(Match(1, ("corners", 9.5m, 1.25m), ("cards", 4.5m, 1.05m), ("goals", 3.5m, 1.15m)), rules));
    }

    [Fact]
    public void Interval_choice_takes_only_the_matching_interval_no_event_outcome()
    {
        var rules = new BettingRules { Markets = [new() { Market = "goals", Side = "Under", Leagues = ["League A"], IntervalFrom = 1, IntervalTo = 10 }] };
        var m = new MatchInfo("m1", "League A", "H", "A", T0,
        [
            new Selection("goals", "Under", 2.5m, 1.15m, "whole-match"),            // whole match: ignored by an interval choice
            new Selection("goals", "Under", 0.5m, 1.18m, "16-30", "16-30"),          // other interval: ignored
            new Selection("goals", "Under", 0.5m, 1.12m, "1-10", "1-10"),
        ]);
        Assert.Equal("1-10", SlipBuilder.BestPick(m, rules)!.Selection.Interval);
    }

    [Fact]
    public void Interval_out_of_odds_range_falls_through_to_next_choice()
    {
        var rules = new BettingRules
        {
            Markets = [new() { Market = "goals", Side = "Under", Leagues = ["League A"], IntervalFrom = 1, IntervalTo = 15 }, new() { Market = "corners", Side = "Under", Leagues = ["League A"] }],
        };
        var m = new MatchInfo("m1", "League A", "H", "A", T0,
            [new Selection("goals", "Under", 0.5m, 1.26m, "1-15", "1-15"), new Selection("corners", "Under", 11.5m, 1.14m, "c")]);
        var pick = SlipBuilder.BestPick(m, rules)!;
        Assert.Equal("corners", pick.Selection.Market);
        Assert.Null(pick.Selection.Interval);
    }

    [Fact]
    public void Whole_match_choice_ignores_interval_outcomes()
    {
        var rules = new BettingRules { Markets = [new() { Market = "goals", Side = "Under", Leagues = ["League A"] }] };
        var m = new MatchInfo("m1", "League A", "H", "A", T0, [new Selection("goals", "Under", 0.5m, 1.15m, "1-10", "1-10")]);
        Assert.Null(SlipBuilder.BestPick(m, rules));
    }

    [Fact]
    public void Market_row_only_applies_to_its_own_leagues()
    {
        var rules = new BettingRules
        {
            Markets =
            [
                new() { Market = "corners", Side = "Under", Leagues = ["League B"] }, // 1st choice, but not for League A
                new() { Market = "cards", Side = "Under", Leagues = ["league a"] },   // league names match case-insensitively
            ],
        };
        var inA = Match(1, ("corners", 9.5m, 1.15m), ("cards", 4.5m, 1.18m));
        Assert.Equal("cards", SlipBuilder.BestPick(inA, rules)!.Selection.Market);
        var inC = inA with { Id = "m2", League = "League C" };
        Assert.Null(SlipBuilder.BestPick(inC, rules)); // no market row registered for League C
    }

    [Fact]
    public void Explain_gives_the_same_pick_as_BestPick_with_reasons()
    {
        var rules = new BettingRules { Markets = UnderCornersAndCards() };
        var m = Match(1, ("corners", 9.5m, 1.25m), ("cards", 4.5m, 1.15m));
        var d = SlipBuilder.Explain(m, rules);
        Assert.Equal(SlipBuilder.BestPick(m, rules)!.Selection, d.Pick!.Selection);
        Assert.Contains("1st Corners Under: no line", d.Steps[0]);
        Assert.Contains("2nd Yellow cards Under: ✓ Under 4.5 @ 1.15", d.Steps[1]);

        var none = SlipBuilder.Explain(m with { League = "League Z" }, rules);
        Assert.Null(none.Pick);
        Assert.Equal("No market row lists League Z.", none.Steps.Single());
    }

    [Fact]
    public void Value_set_by_user_takes_exactly_that_line()
    {
        var rules = new BettingRules { Markets = [new() { Market = "corners", Side = "Under", Leagues = ["League A"], Line = 10.5m }] };
        var m = Match(1, ("corners", 9.5m, 1.12m), ("corners", 10.5m, 1.18m), ("corners", 11.5m, 1.10m));
        Assert.Equal(10.5m, SlipBuilder.BestPick(m, rules)!.Selection.Line); // not the highest line in range (11.5)
        var noSuchLine = Match(2, ("corners", 9.5m, 1.12m));
        Assert.Null(SlipBuilder.BestPick(noSuchLine, rules));
    }

    [Fact]
    public void Interval_value_picks_that_line_and_no_value_means_under_half()
    {
        var withValue = new BettingRules { Markets = [new() { Market = "goals", Side = "Under", Leagues = ["League A"], IntervalFrom = 1, IntervalTo = 10, Line = 1.5m }] };
        var noValue = new BettingRules { Markets = [new() { Market = "goals", Side = "Under", Leagues = ["League A"], IntervalFrom = 1, IntervalTo = 10 }] };
        var m = new MatchInfo("m1", "League A", "H", "A", T0,
            [new Selection("goals", "Under", 0.5m, 1.14m, "a", "1-10"), new Selection("goals", "Under", 1.5m, 1.11m, "b", "1-10")]);
        Assert.Equal(1.5m, SlipBuilder.BestPick(m, withValue)!.Selection.Line);
        Assert.Equal(0.5m, SlipBuilder.BestPick(m, noValue)!.Selection.Line);
    }

    [Fact]
    public void Value_out_of_odds_range_falls_through()
    {
        var rules = new BettingRules
        {
            Markets = [new() { Market = "corners", Side = "Under", Leagues = ["League A"], Line = 9.5m }, new() { Market = "cards", Side = "Under", Leagues = ["League A"] }],
        };
        var m = Match(1, ("corners", 9.5m, 1.49m), ("cards", 5.5m, 1.15m));
        Assert.Equal("cards", SlipBuilder.BestPick(m, rules)!.Selection.Market);
    }

    [Fact]
    public void Result_market_takes_the_draw_after_the_interval()
    {
        var rules = new BettingRules
        {
            Markets =
            [
                new() { Market = "goals", Side = "Under", Leagues = ["League A"], IntervalFrom = 1, IntervalTo = 10 },  // 1st: no goal by minute 10
                new() { Market = "result", Side = "Draw", Leagues = ["League A"], IntervalFrom = 1, IntervalTo = 10 }, // 2nd: draw at minute 10
            ],
        };
        // Riestra v Central Cordoba on Leonbet: no "Total After 10 Minutes", but "10 Minute Result" X @ 1.13.
        var m = new MatchInfo("m1", "League A", "H", "A", T0, [new Selection("result", "Draw", 0, 1.13m, "r", "1-10", "10 Minute Result: X (draw)")]);
        var d = SlipBuilder.Explain(m, rules);
        Assert.Equal("result", d.Pick!.Selection.Market);
        Assert.Contains("not offered", d.Steps[0]);
        Assert.Contains("10 Minute Result: X (draw) @ 1.13", d.Steps[1]);
    }

    [Fact]
    public void BestPick_only_uses_the_accounts_markets()
    {
        var cornersOnly = new BettingRules { Markets = [new() { Market = "corners", Side = "Under", Leagues = ["League A"] }] };
        var m = Match(1, ("cards", 5.5m, 1.12m), ("corners", 9.5m, 1.25m)); // corners odds out of range
        Assert.Null(SlipBuilder.BestPick(m, cornersOnly));
    }

    [Fact]
    public void BestPick_null_when_match_has_no_cards_or_corners_market() =>
        Assert.Null(SlipBuilder.BestPick(Match(1, ("goals", 1.5m, 1.15m)), Rules));

    [Fact]
    public void Build_lands_in_range_with_at_most_six_distinct_matches()
    {
        var matches = Enumerable.Range(1, 20)
            .Select(i => Match(i, ("cards", 2.5m, 1.10m + (i % 10) * 0.01m), ("corners", 7.5m, 1.11m + (i % 7) * 0.01m)))
            .ToList();
        var plan = SlipBuilder.Build(matches, Rules)!;
        Assert.InRange(plan.CombinedOdds, 2.20m, 2.50m);
        Assert.InRange(plan.Picks.Count, 1, 6);
        Assert.Equal(plan.Picks.Count, plan.Picks.Select(p => p.Match.Id).Distinct().Count());
        Assert.All(plan.Picks, p => Assert.InRange(p.Odds, 1.10m, 1.20m));
    }

    [Fact]
    public void Build_prefers_lowest_combined_odds_that_reaches_minimum()
    {
        // Six picks at 1.15 = 2.313; five at 1.18-1.20 also fit, but are riskier (higher product).
        var matches = Enumerable.Range(1, 6).Select(i => Match(i, ("corners", 7.5m, 1.15m)))
            .Concat(Enumerable.Range(7, 5).Select(i => Match(i, ("cards", 3.5m, 1.20m))))
            .ToList();
        var plan = SlipBuilder.Build(matches, Rules)!;
        Assert.True(plan.CombinedOdds >= 2.20m);
        Assert.True(plan.CombinedOdds <= 2.32m, $"expected the safest slip, got {plan.CombinedOdds}");
    }

    [Fact]
    public void Build_returns_null_when_range_unreachable()
    {
        // 1.10^6 = 1.77 < 2.20
        var matches = Enumerable.Range(1, 10).Select(i => Match(i, ("corners", 7.5m, 1.10m))).ToList();
        Assert.Null(SlipBuilder.Build(matches, Rules));
    }

    [Fact]
    public void Build_returns_null_with_too_few_matches()
    {
        // 4 picks at the 1.20 cap = 2.07 < 2.20
        var matches = Enumerable.Range(1, 4).Select(i => Match(i, ("corners", 7.5m, 1.20m))).ToList();
        Assert.Null(SlipBuilder.Build(matches, Rules));
    }

    [Fact]
    public void InLeagues_filters_by_registered_leagues_case_insensitive()
    {
        var a = Match(1, ("corners", 7.5m, 1.15m));
        var b = a with { Id = "m2", League = "League B" };
        var kept = SlipBuilder.InLeagues([a, b], [" league a "]).ToList();
        Assert.Single(kept);
        Assert.Equal("m1", kept[0].Id);
    }
}
