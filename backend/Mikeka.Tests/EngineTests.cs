using Mikeka.Api.Bookmaker;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Tests;

public class EngineTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 5, 0, 0, TimeSpan.Zero)); // 08:00 EAT
    private readonly FakeBookmaker _book;
    private readonly FakeNotifier _mail = new();
    private readonly MikekaDb _db;
    private readonly AccountSecrets _secrets = SecretsTests.Create();
    private readonly string _excel = Path.Combine(Path.GetTempPath(), $"mikeka-test-{Guid.NewGuid():N}.xlsx");
    private readonly BettingRules _rules = new() { DryRun = false, PlaceBets = true };
    private readonly Account _account;

    public EngineTests()
    {
        _book = new FakeBookmaker(_clock);
        _db = new MikekaDb(new DbContextOptionsBuilder<MikekaDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _account = new Account { Name = "t", Url = "https://example.test/en/line/football", Username = "u", Leagues = ["League A"], Markets = StrategyTests.UnderCornersAndCards(), PasswordProtected = _secrets.Protect("pw") };
        _db.Accounts.Add(_account);
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); File.Delete(_excel); }

    private BettingEngine Engine() => new(_db, _book, _secrets, _mail,
        new ExcelLog(_db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ExcelLogPath"] = _excel }).Build()),
        Options.Create(_rules), _clock, NullLogger<BettingEngine>.Instance);


    private Task<RunResult> Run(bool manual = false) => Engine().RunAsync(_account.Id, manual, default);

    private void NextDay() => _clock.Advance(TimeSpan.FromDays(1));

    [Fact]
    public async Task Places_slip_with_base_stake_and_writes_excel()
    {
        var r = await Run();
        Assert.Equal("Placed", r.Outcome);
        var slip = await _db.Slips.Include(s => s.Picks).SingleAsync();
        Assert.Equal(1000, slip.Stake);
        Assert.InRange(slip.CombinedOdds, 2.20m, 2.50m);
        Assert.InRange(slip.Picks.Count, 1, 6);
        Assert.True(File.Exists(_excel));
        Assert.Equal("pw", _book.LastPassword);
    }

    [Fact]
    public async Task Waits_while_previous_slip_unsettled_then_doubles_after_loss()
    {
        await Run();
        NextDay();
        _book.NextOutcome = BetOutcome.Pending;
        Assert.Equal("Waiting", (await Run()).Outcome);
        Assert.Equal(1, await _db.Slips.CountAsync());

        _book.NextOutcome = BetOutcome.Lost;
        Assert.Equal("Placed", (await Run()).Outcome);
        var latest = await _db.Slips.OrderByDescending(s => s.Id).FirstAsync();
        Assert.Equal(2000, latest.Stake);
    }

    [Fact]
    public async Task Sequence_lose_lose_lose_win_returns_to_base_stake_and_emails_at_three()
    {
        var expected = new[] { 1000m, 2000m, 4000m, 8000m, 1000m };
        var outcomes = new[] { BetOutcome.Lost, BetOutcome.Lost, BetOutcome.Lost, BetOutcome.Won };
        for (int i = 0; i < expected.Length; i++)
        {
            if (i > 0) { _book.NextOutcome = outcomes[i - 1]; NextDay(); }
            await Run();
            Assert.Equal(expected[i], (await _db.Slips.OrderByDescending(s => s.Id).FirstAsync()).Stake);
        }
        Assert.Single(_mail.Sent, m => m.subject.Contains("3 losses"));
    }

    [Fact]
    public async Task Uses_the_accounts_own_base_stake_and_doubles_it()
    {
        _account.BaseStake = 2500;
        await _db.SaveChangesAsync();
        await Run();
        _book.NextOutcome = BetOutcome.Lost; NextDay(); await Run();
        _book.NextOutcome = BetOutcome.Won; NextDay(); await Run();
        var stakes = await _db.Slips.OrderBy(s => s.Id).Select(s => s.Stake).ToListAsync();
        Assert.Equal([2500m, 5000m, 2500m], stakes);
    }

    [Fact]
    public async Task Stops_after_four_losses_until_reset()
    {
        _book.NextOutcome = BetOutcome.Lost;
        for (int i = 0; i < 5; i++) { await Run(); NextDay(); }
        var r = await Run();
        Assert.Equal("Stopped", r.Outcome);
        Assert.True((await _db.Accounts.SingleAsync()).Stopped);
        Assert.Equal(4, await _db.Slips.CountAsync());
        Assert.Contains(_mail.Sent, m => m.subject.StartsWith("STOPPED"));
    }

    [Fact]
    public async Task No_bet_when_balance_zero()
    {
        _book.Balance = 0;
        var r = await Run();
        Assert.Equal("Skipped", r.Outcome);
        Assert.Equal(0, _book.Placed);
    }

    [Fact]
    public async Task Emails_top_up_request_when_balance_below_stake_and_retries_on_manual_run()
    {
        _book.Balance = 500;
        Assert.Equal("Skipped", (await Run()).Outcome);
        Assert.Contains(_mail.Sent, m => m.subject.Contains("top up"));
        Assert.Equal("Done", (await Run()).Outcome); // scheduler does not retry the same day

        _book.Balance = 5000;
        Assert.Equal("Placed", (await Run(manual: true)).Outcome);
    }

    [Fact]
    public async Task No_bet_when_registered_league_has_no_matches()
    {
        _account.Leagues = ["Other League"];
        await _db.SaveChangesAsync();
        var r = await Run();
        Assert.Equal("Skipped", r.Outcome);
        Assert.Contains("Other League", r.Message);
    }

    [Fact]
    public async Task Spreads_over_next_days_when_today_cannot_reach_odds()
    {
        _book.TodayOnly = false;
        _book.MatchesToday = 2; // 2 today + more tomorrow
        var r = await Run();
        Assert.Equal("Placed", r.Outcome);
        var slip = await _db.Slips.Include(s => s.Picks).SingleAsync();
        Assert.Contains(slip.Picks, p => p.Kickoff > _clock.GetUtcNow().UtcDateTime.AddHours(19));
    }

    [Fact]
    public async Task Dry_run_builds_slip_without_placing()
    {
        _rules.DryRun = true;
        var r = await Run();
        Assert.Equal("DryRun", r.Outcome);
        Assert.Equal(0, _book.Placed);
    }

    [Fact]
    public async Task Unconfirmed_bet_is_kept_pending_emailed_and_blocks_the_next_bet()
    {
        _book.PlaceError = new BetUnconfirmedException("no bet number");
        var r = await Run();
        Assert.Equal("Unconfirmed", r.Outcome);
        var slip = await _db.Slips.SingleAsync();
        Assert.Equal(SlipStatus.Pending, slip.Status);
        Assert.Null(slip.BetReference);
        Assert.Contains(_mail.Sent, m => m.subject.StartsWith("Check bet"));

        _book.PlaceError = null;
        NextDay();
        r = await Run();
        Assert.Equal("Waiting", r.Outcome);
        Assert.Equal(1, await _db.Slips.CountAsync());
    }

    [Fact]
    public async Task Error_before_placing_saves_no_slip()
    {
        _book.PlaceError = new InvalidOperationException("odds moved");
        var r = await Run();
        Assert.Equal("NotPlaced", r.Outcome);
        Assert.Equal(0, await _db.Slips.CountAsync());
    }

    [Fact]
    public async Task Real_bets_only_on_coldbet()
    {
        _account.Site = "1win";
        await _db.SaveChangesAsync();
        var r = await Run();
        Assert.Equal("NotSupported", r.Outcome);
        Assert.Equal(0, _book.Placed);
    }

    [Fact]
    public async Task Manual_loss_settles_and_doubles_the_stake()
    {
        _book.PlaceError = new BetUnconfirmedException("no bet number");
        await Run();
        var slip = await _db.Slips.SingleAsync();
        await Engine().SettleManuallyAsync(slip.Id, won: false, default);
        Assert.Equal(SlipStatus.Lost, slip.Status);
        Assert.Equal(1, _account.LossStreak);

        _book.PlaceError = null;
        NextDay();
        var r = await Run();
        Assert.Equal("Placed", r.Outcome);
        Assert.Equal(2000, (await _db.Slips.OrderBy(s => s.Id).LastAsync()).Stake);
    }

    private sealed class FakeNotifier : INotifier
    {
        public List<(string subject, string body)> Sent { get; } = [];
        public Task SendAsync(string subject, string body, CancellationToken ct) { Sent.Add((subject, body)); return Task.CompletedTask; }
    }

    private sealed class FakeBookmaker(TimeProvider clock) : IBookmakerFactory, IBookmakerClient
    {
        public decimal Balance { get; set; } = 100_000;
        public BetOutcome NextOutcome { get; set; } = BetOutcome.Won;
        public int Placed { get; private set; }
        public string? LastPassword { get; private set; }
        public bool TodayOnly { get; set; } = true;
        public int MatchesToday { get; set; } = 12;

        public Task<IBookmakerClient> CreateAsync(Account account, string password, CancellationToken ct)
        { LastPassword = password; return Task.FromResult<IBookmakerClient>(this); }

        public Task LoginAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<decimal> GetBalanceAsync(CancellationToken ct) => Task.FromResult(Balance);

        public Task<IReadOnlyList<MatchInfo>> GetMatchesAsync(IReadOnlyCollection<string> leagues, IReadOnlyList<MarketChoice> markets, DateTime untilUtc, CancellationToken ct)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var list = Enumerable.Range(1, MatchesToday).Select(i => StrategyTests.Match(i, ("corners", 7.5m, 1.15m), ("cards", 3.5m, 1.18m))
                with { Kickoff = now.AddHours(1 + i * 0.5) }).ToList();
            if (!TodayOnly)
                list.AddRange(Enumerable.Range(100, 8).Select(i => StrategyTests.Match(i, ("corners", 8.5m, 1.16m)) with { Kickoff = now.AddHours(24 + i % 10) }));
            return Task.FromResult<IReadOnlyList<MatchInfo>>(list.Where(m => m.Kickoff <= untilUtc).ToList());
        }

        public Exception? PlaceError { get; set; }

        public Task FillSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct) => Task.CompletedTask;

        public Task<string> PlaceSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
        {
            if (PlaceError is not null) throw PlaceError;
            Placed++;
            return Task.FromResult($"BET{Placed}");
        }

        public Task<BetOutcome> GetOutcomeAsync(string betReference, CancellationToken ct) => Task.FromResult(NextOutcome);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
