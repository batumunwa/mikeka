using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mikeka.Api.Bookmaker;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;

namespace Mikeka.Api.Services;

public record RunResult(int AccountId, string Outcome, string Message, int? SlipId = null);

/// <summary>
/// One run for one account, in the order the requirements give:
/// settle yesterday's slip (wait if unsettled) → stop-loss → one slip per day → balance →
/// stake → matches → build slip in odds range → place → log.
/// </summary>
public class BettingEngine(
    MikekaDb db,
    IBookmakerFactory bookmakers,
    AccountSecrets secrets,
    INotifier notifier,
    ExcelLog excel,
    IOptions<BettingRules> rulesOptions,
    TimeProvider clock,
    ILogger<BettingEngine> log)
{
    private readonly BettingRules _rules = rulesOptions.Value;

    /// <param name="manual">Manual "Run now" retries a day that was skipped (e.g. after you top up the balance).</param>
    public async Task<RunResult> RunAsync(int accountId, bool manual, CancellationToken ct)
    {
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, ct);
        var today = Eat.Today(clock);

        if (!account.IsActive) return await Done(account, "Inactive", "Account is disabled.");
        if (account.Stopped)
            return await Done(account, "Stopped", $"Stopped after {account.LossStreak} consecutive losses. Reset the account to resume.");

        var pending = await db.Slips.Where(s => s.AccountId == accountId && s.Status == SlipStatus.Pending)
            .OrderBy(s => s.CreatedAt).ToListAsync(ct);
        var todays = await db.Slips.Where(s => s.AccountId == accountId && s.BetDay == today).ToListAsync(ct);
        bool alreadyBetToday = todays.Any(s => s.Status is SlipStatus.Pending or SlipStatus.Won or SlipStatus.Lost); // drafts are not bets
        // A "no bet" day is a "Skipped: …" activity-log line (no slip); a dry-run slip also counts as today handled.
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), Eat.Zone);
        bool skippedToday = todays.Any(s => s.Status == SlipStatus.Skipped)
            || await db.RunLogs.AnyAsync(l => l.AccountId == accountId && l.At >= todayStartUtc && l.Message.StartsWith("Skipped:"), ct);

        // Nothing to do without touching the site: today's slip is in and nothing waits for a result.
        if (pending.Count == 0 && (alreadyBetToday || (skippedToday && !manual)))
            return new RunResult(accountId, "Done", "Today's slip is already handled.");

        // A slip sent to the site without a bet number can't be checked automatically, and betting again could double it.
        if (pending.FirstOrDefault(s => string.IsNullOrEmpty(s.BetReference)) is { } unconfirmed)
            return await Done(account, "Waiting",
                $"Slip #{unconfirmed.Id} ({unconfirmed.BetDay}) was sent to {account.Site} without a bet number. " +
                "Check 'My bets' on the site, then mark it Won or Lost in the dashboard (or delete it if it was never placed).");
        if (!_rules.DryRun && !new[] { "coldbet", "leonbet", "1win" }.Contains(account.Site, StringComparer.OrdinalIgnoreCase))
            return await Done(account, "NotSupported", $"Filling and placing bets is built for Coldbet, Leonbet and 1win only; {account.Site} slips are entered by hand.");
        // Only Coldbet's bet history is read by the system; a placed Leonbet/1win slip waits for the user to mark it.
        if (pending.FirstOrDefault() is { } open && !account.Site.Equals("coldbet", StringComparison.OrdinalIgnoreCase))
            return await Done(account, "Waiting",
                $"Slip #{open.Id} ({open.BetDay}) is placed on {account.Site}; the system cannot read {account.Site} results yet. " +
                "Mark it Won or Lost here once its matches are over.");

        await using var client = await bookmakers.CreateAsync(account, secrets.Unprotect(account.PasswordProtected), ct);
        await client.LoginAsync(ct);

        // 1) Settle earlier slips. If any is still being played, wait: the stake depends on its result.
        foreach (var slip in pending)
        {
            var outcome = await client.GetOutcomeAsync(slip.BetReference!, ct);
            if (outcome == BetOutcome.Pending)
                return await Done(account, "Waiting", $"Slip #{slip.Id} ({slip.BetDay}) is not settled yet; will check again.");
            await SettleAsync(account, slip, outcome == BetOutcome.Won, await client.GetBalanceAsync(ct), ct);
            if (account.Stopped)
                return await Done(account, "Stopped", $"{account.LossStreak} consecutive losses. Betting stopped.");
        }

        if (alreadyBetToday) return await Done(account, "Done", "Today's slip is already placed.");
        if (TimeOnly.FromDateTime(Eat.Now(clock)) < _rules.RunAt && !manual)
            return await Done(account, "Early", $"Slips are generated from {_rules.RunAt:HH:mm} EAT.");
        if (skippedToday && !manual) return await Done(account, "Done", "Today was skipped; use Run now to retry.");

        // 2) Balance and stake.
        var balance = await client.GetBalanceAsync(ct);
        account.LastBalance = balance;
        var stake = StakeCalculator.NextStake(account);
        if (balance <= 0)
            return await Skip(account, today, stake, balance, "Balance is zero. No bet.", ct);
        if (balance < stake)
        {
            await notifier.SendAsync($"Please top up {account.Username}",
                $"Account {account.Username} has {balance:N0} {account.Currency} but the next slip needs {stake:N0} {account.Currency} " +
                $"(loss streak {account.LossStreak}). Please update the balance; then press \"Run now\" in the dashboard.", ct);
            return await Skip(account, today, stake, balance, $"Balance {balance:N0} is less than stake {stake:N0}. Top-up requested by email.", ct);
        }

        // 3) Matches: today first; widen day by day until the combined odds fit.
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var endOfToday = TimeZoneInfo.ConvertTimeToUtc(today.AddDays(1).ToDateTime(TimeOnly.MinValue), Eat.Zone);
        var accountRules = _rules.ForAccount(account);
        var matches = await client.GetMatchesAsync(account.Leagues, account.Markets, endOfToday.AddDays(_rules.MaxDaysAhead - 1), ct);
        matches = SlipBuilder.InLeagues(matches, account.Leagues).Where(m => m.Kickoff > nowUtc).ToList();
        if (!matches.Any(m => m.Selections.Any(x => accountRules.Markets.Any(c => c.Market == x.Market && c.Side == x.Side))))
            return await Skip(account, today, stake, balance,
                $"No matches offering {Describe(account)} in {string.Join(", ", account.Leagues)}. No bet.", ct);

        SlipPlan? plan = null;
        for (int d = 0; d < _rules.MaxDaysAhead && plan is null; d++)
            plan = SlipBuilder.Build(matches.Where(m => m.Kickoff < endOfToday.AddDays(d)), accountRules);
        if (plan is null)
            return await Skip(account, today, stake, balance,
                $"No combination of up to {_rules.MaxMatches} picks at {_rules.MinPickOdds}–{_rules.MaxPickOdds} reaches {_rules.MinCombinedOdds}–{_rules.MaxCombinedOdds}. No bet.", ct);

        // Last guard: never send two selections of one match to a site (sites refuse them in one accumulator).
        if (plan.Picks.GroupBy(p => SlipBuilder.MatchKey(p.Match)).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return await Done(account, "NotFilled", $"The slip has {twice.First().Match.Home} v {twice.First().Match.Away} twice; nothing was sent to {account.Site}.");

        // 4) Place.
        var newSlip = new Slip
        {
            AccountId = account.Id,
            BetDay = today,
            Stake = stake,
            CombinedOdds = plan.CombinedOdds,
            PotentialReturn = Math.Round(stake * plan.CombinedOdds, 2),
            BalanceBefore = balance,
            Picks = plan.Picks.Select(p => new SlipPick
            {
                MatchId = p.Match.Id, League = p.Match.League, Home = p.Match.Home, Away = p.Match.Away,
                Kickoff = p.Match.Kickoff, Market = p.Selection.Market, Side = p.Selection.Side,
                Line = p.Selection.Line, Odds = p.Selection.Odds, Interval = p.Selection.Interval, Label = p.Selection.Label,
            }).ToList(),
        };

        bool filledOnly = false, stakeTyped = false;
        if (_rules.DryRun)
        {
            newSlip.Status = SlipStatus.Skipped;
            newSlip.Note = "DRY RUN: slip built but not placed.";
        }
        else if (!_rules.PlaceBets)
        {
            // The odds are clicked into the site's bet slip; the user places it. Saved as Pending without a bet number, so no
            // further slip is built for this account until the user marks it Won/Lost (or deletes it if not placed).
            try
            {
                stakeTyped = await client.FillSlipAsync(plan.Picks, stake, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await Done(account, "NotFilled", $"Bet slip not filled: {ex.Message}");
            }
            filledOnly = true;
            newSlip.Status = SlipStatus.Pending;
            newSlip.Note = $"In the {account.Site} bet slip, NOT placed by the system: stake {stake:N0} {(stakeTyped ? "typed in" : "NOT typed, type it yourself")}, you click Place. " +
                           "Then mark it Won or Lost here (or delete it if you did not place it).";
        }
        else
        {
            try
            {
                newSlip.BetReference = await client.PlaceSlipAsync(plan.Picks, stake, ct);
                // The balance after the stake left it (kept in the account's balance history).
                try { account.LastBalance = await client.GetBalanceAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Balance after placing not read: {Error}", ex.Message); }
            }
            catch (BetUnconfirmedException ex)
            {
                // "Place" was clicked: the bet may exist. Keep the slip as Pending (blocks further bets) and ask for a check.
                newSlip.Note = "UNCONFIRMED: " + ex.Message;
                await notifier.SendAsync($"Check bet on {account.Site} ({account.Username})",
                    $"A slip of {plan.Picks.Count} picks @ {plan.CombinedOdds}, stake {stake:N0} {account.Currency}, was sent to {account.Site} " +
                    $"but no bet number was seen.\n{ex.Message}\n\nCheck 'My bets' on the site, then mark the slip Won or Lost in the dashboard " +
                    "(or delete it if it was never placed). No new bets are placed for this account until then.", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await Done(account, "NotPlaced", $"Slip not placed: {ex.Message}");
            }
            newSlip.Status = SlipStatus.Pending;
        }
        db.Slips.Add(newSlip);
        await db.SaveChangesAsync(ct);
        await excel.WriteFileAsync(ct);

        if (filledOnly)
            return await Done(account, "Filled",
                $"Bet slip filled on {account.Site} (not placed) #{newSlip.Id}: {plan.Picks.Count} picks @ {plan.CombinedOdds}, stake {stake:N0} {account.Currency} " +
                (stakeTyped ? "typed into the stake box" : "NOT typed (type it yourself)") + ". The tab is open in Chrome: check it and click Place.", newSlip.Id);

        var unconfirmedSlip = !_rules.DryRun && newSlip.BetReference is null;
        var summary = $"{(_rules.DryRun ? "Dry-run slip" : unconfirmedSlip ? "UNCONFIRMED slip" : "Placed slip")} #{newSlip.Id}: {plan.Picks.Count} picks @ {plan.CombinedOdds}, stake {stake:N0} {account.Currency}" +
                      (newSlip.BetReference is null ? "" : $", bet {newSlip.BetReference}") +
                      (unconfirmedSlip ? ". No bet number seen: check 'My bets' on the site." : "");
        return await Done(account, _rules.DryRun ? "DryRun" : unconfirmedSlip ? "Unconfirmed" : "Placed", summary, newSlip.Id);
    }

    /// <summary>
    /// Result entered by the user for a Pending slip (e.g. one placed without a bet number): same effect as an automatic
    /// settlement — loss streak, stop-loss and alerts.
    /// </summary>
    public async Task SettleManuallyAsync(int slipId, bool won, CancellationToken ct)
    {
        var slip = await db.Slips.SingleOrDefaultAsync(s => s.Id == slipId, ct) ?? throw new KeyNotFoundException("Slip not found.");
        if (slip.Status != SlipStatus.Pending)
            throw new InvalidOperationException($"Slip #{slipId} is {slip.Status}; only Pending slips can be marked Won or Lost.");
        var account = await db.Accounts.SingleAsync(a => a.Id == slip.AccountId, ct);
        slip.Note = $"{slip.Note} Marked {(won ? "Won" : "Lost")} by the user.".Trim();
        await SettleAsync(account, slip, won, account.LastBalance, ct);
    }

    private async Task SettleAsync(Account account, Slip slip, bool won, decimal? balance, CancellationToken ct)
    {
        slip.Status = won ? SlipStatus.Won : SlipStatus.Lost;
        slip.SettledAt = clock.GetUtcNow().UtcDateTime;
        slip.BalanceAfter = balance;
        account.LastBalance = balance ?? account.LastBalance;
        account.LossStreak = won ? 0 : account.LossStreak + 1;
        await Log(account, $"Slip #{slip.Id} {slip.Status}. Loss streak now {account.LossStreak}.");

        if (!won && account.LossStreak >= _rules.StopAfterLosses)
        {
            account.Stopped = true;
            await notifier.SendAsync($"STOPPED: {account.LossStreak} losses in a row ({account.Username})",
                $"Account {account.Username} lost {account.LossStreak} slips in a row. Betting is stopped.\n" +
                $"Balance: {balance:N0} {account.Currency}. Reset the account in the dashboard to resume at {account.BaseStake:N0}.", ct);
        }
        else if (!won && account.LossStreak == _rules.AlertAfterLosses)
        {
            await notifier.SendAsync($"{account.LossStreak} losses in a row ({account.Username})",
                $"Account {account.Username} has lost {account.LossStreak} slips in a row.\n" +
                $"Next stake: {StakeCalculator.NextStake(account):N0} {account.Currency}. Balance: {balance:N0}.\n" +
                $"Betting stops automatically after {_rules.StopAfterLosses} losses.", ct);
        }
        await db.SaveChangesAsync(ct);
        await excel.WriteFileAsync(ct);
    }

    private static string Describe(Account a) => string.Join(" / ", a.Markets.Select(m =>
        m.IntervalKey is null ? $"total {m.Market} {m.Side}" : $"no {m.Market} in minutes {m.IntervalKey}"));

    /// <summary>A "no bet" day: recorded in the activity log only (it is not a slip). The log line also marks the day as handled.</summary>
    private Task<RunResult> Skip(Account account, DateOnly day, decimal stake, decimal balance, string reason, CancellationToken ct) =>
        Done(account, "Skipped", reason);

    private async Task<RunResult> Done(Account account, string outcome, string message, int? slipId = null)
    {
        await Log(account, $"{outcome}: {message}");
        await db.SaveChangesAsync();
        return new RunResult(account.Id, outcome, message, slipId);
    }

    private Task Log(Account account, string message)
    {
        log.LogInformation("[{User}] {Message}", account.Username, message);
        db.RunLogs.Add(new RunLog { AccountId = account.Id, Message = message, At = clock.GetUtcNow().UtcDateTime });
        return Task.CompletedTask;
    }
}
