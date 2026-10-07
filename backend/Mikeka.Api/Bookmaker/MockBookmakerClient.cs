using Mikeka.Api.Domain;
using System.Collections.Concurrent;
using Mikeka.Api.Services;

namespace Mikeka.Api.Bookmaker;

/// <summary>
/// Fake bookmaker for testing the whole flow without touching the real site.
/// Generates random matches; bets settle randomly 1 hour after placement.
/// </summary>
public class MockBookmakerClient(TimeProvider clock) : IBookmakerClient
{
    private static readonly ConcurrentDictionary<string, (DateTime placed, decimal stake, decimal odds)> Bets = new();
    private static readonly ConcurrentDictionary<string, BetOutcome> Settled = new();
    private static decimal _balance = 50_000;
    private readonly Random _rng = new();

    public Task LoginAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<decimal> GetBalanceAsync(CancellationToken ct) => Task.FromResult(_balance);

    public Task<IReadOnlyList<MatchInfo>> GetMatchesAsync(IReadOnlyCollection<string> leagues, IReadOnlyList<MarketChoice> markets, DateTime untilUtc, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var list = new List<MatchInfo>();
        for (int i = 0; i < 25; i++)
        {
            var kickoff = now.AddHours(2 + i * 3);
            if (kickoff > untilUtc) break;
            var sels = new List<Selection>();
            foreach (var choice in markets.Where(c => c.IntervalKey is not null))
            {
                if (_rng.NextDouble() < 0.25) continue; // interval not offered for this match
                var odds = Math.Round(1.05m + (decimal)_rng.NextDouble() * 0.30m, 2);
                sels.Add(new Selection(choice.Market, "Under", 0.5m, odds, $"m{i}-{choice.Market}-no-{choice.IntervalKey}", choice.IntervalKey));
            }
            foreach (var market in markets.Where(c => c.IntervalKey is null).Select(c => c.Market).Distinct())
            {
                if (_rng.NextDouble() < 0.25) continue; // some matches lack the market
                decimal baseLine = market switch { "goals" => 1.5m, "cards" => 2.5m, "fouls" => 20.5m, _ => 6.5m };
                for (int k = 0; k < 5; k++)
                {
                    var line = baseLine + k;
                    // Higher line: Over gets dearer, Under gets cheaper (safer).
                    var over = Math.Round(1.04m + k * 0.07m + (decimal)_rng.NextDouble() * 0.05m, 2);
                    var under = Math.Round(1.04m + (4 - k) * 0.07m + (decimal)_rng.NextDouble() * 0.05m, 2);
                    sels.Add(new Selection(market, "Over", line, over, $"m{i}-{market}-over-{line}"));
                    sels.Add(new Selection(market, "Under", line, under, $"m{i}-{market}-under-{line}"));
                }
            }
            var league = leagues.Count == 0 ? "Mock League" : leagues.ElementAt(i % leagues.Count); // spread over the registered leagues
            list.Add(new MatchInfo($"m{i}", league, $"Home {i}", $"Away {i}", kickoff, sels));
        }
        return Task.FromResult<IReadOnlyList<MatchInfo>>(list);
    }

    public Task<string> PlaceSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
    {
        var id = "MOCK-" + Guid.NewGuid().ToString("N")[..8];
        _balance -= stake;
        Bets[id] = (clock.GetUtcNow().UtcDateTime, stake, picks.Aggregate(1m, (a, p) => a * p.Odds));
        return Task.FromResult(id);
    }

    public Task<bool> FillSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct) => Task.FromResult(true);

    public Task<BetOutcome> GetOutcomeAsync(Slip slip, CancellationToken ct)
    {
        var betReference = slip.BetReference ?? "";
        if (Settled.TryGetValue(betReference, out var done)) return Task.FromResult(done);
        if (!Bets.TryGetValue(betReference, out var bet)) return Task.FromResult(BetOutcome.Lost);
        if (clock.GetUtcNow().UtcDateTime < bet.placed.AddHours(1)) return Task.FromResult(BetOutcome.Pending);
        var outcome = _rng.NextDouble() < 0.6 ? BetOutcome.Won : BetOutcome.Lost;
        if (Settled.TryAdd(betReference, outcome) && outcome == BetOutcome.Won) _balance += bet.stake * bet.odds;
        return Task.FromResult(Settled[betReference]);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
