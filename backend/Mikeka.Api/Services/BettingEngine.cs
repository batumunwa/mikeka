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

    /// <summary>One run per account at a time (scheduler and "Run now" never overlap, so a slip is never placed twice).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> Running = new();

    /// <summary>Choosing a slip (open picks read + build + save) is done by one account at a time, all sites together.</summary>
    private static readonly SemaphoreSlim ChooseLock = new(1, 1);

    /// <param name="manual">"Run now": reads the open slip's result now instead of waiting for its next check time.</param>
    public async Task<RunResult> RunAsync(int accountId, bool manual, CancellationToken ct)
    {
        var gate = Running.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) return new RunResult(accountId, "Busy", "A run for this account is already in progress.");
        try { return await RunLockedAsync(accountId, manual, ct); }
        finally { gate.Release(); }
    }

    private async Task<RunResult> RunLockedAsync(int accountId, bool manual, CancellationToken ct)
    {
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, ct);
        var today = Eat.Today(clock);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        // Default: look again after the check interval. Waiting for a slip's result moves this to its next check time.
        account.NextCheckAt = nowUtc.AddMinutes(_rules.CheckIntervalMinutes);

        if (!account.IsActive) return await Done(account, "Inactive", "Account is disabled.");
        if (account.Stopped)
            return await Done(account, "Stopped", $"Stopped after {account.LossStreak} consecutive losses. Reset the account to resume.");

        var pending = await db.Slips.Include(s => s.Picks).Where(s => s.AccountId == accountId && s.Status == SlipStatus.Pending)
            .OrderBy(s => s.CreatedAt).ToListAsync(ct);

        // A slip sent to the site without a bet number (or filled for the user) can't be checked: betting again could double it.
        if (pending.FirstOrDefault(s => string.IsNullOrEmpty(s.BetReference)) is { } unconfirmed)
            return await Done(account, "Waiting",
                $"Slip #{unconfirmed.Id} ({unconfirmed.BetDay}) was sent to {account.Site} without a bet number. " +
                "Check 'My bets' on the site, then mark it Won or Lost in the dashboard (or delete it if it was never placed).");
        if (!_rules.DryRun && !new[] { "coldbet", "leonbet", "1win" }.Contains(account.Site, StringComparer.OrdinalIgnoreCase))
            return await Done(account, "NotSupported", $"Filling and placing bets is built for Coldbet, Leonbet and 1win only; {account.Site} slips are entered by hand.");

        // 1) An open slip: read its result at its check times (see BettingRules.SettlementChecks). Nothing due = wait, no login.
        if (pending.Count > 0 && !manual)
        {
            var due = pending.Where(ResultDue).ToList();
            if (due.Count == 0)
            {
                var next = pending.Select(NextResultCheck).Min();
                account.NextCheckAt = next;
                return await Done(account, "Waiting",
                    $"Slip #{pending[0].Id} is open; its result is read at {EatTime(next):dd/MM HH:mm} EAT.");
            }
        }

        // A chosen slip waiting for its placing time: nothing to do on the site yet (checked again below if anything changed).
        if (pending.Count == 0 && PreparedSlips.Get(account.Id) is { } held && PlaceAt(held.Plan) > nowUtc.AddMinutes(1)
            && held.Stake == StakeCalculator.NextStake(account) && held.Reuses < PreparedSlips.MaxReuses)
        {
            var heldRules = _rules.ForAccount(account);
            heldRules.Taken = await OpenPicks.LoadAsync(db, account.Id, nowUtc, _rules.MatchMinutes, ct);
            if (held.Plan.Picks.All(p => SlipBuilder.TakenBy(p.Match, p.Selection, heldRules) is null))
                return await Hold(account, held.Plan, held.ChosenAtUtc);
        }

        await using var client = await bookmakers.CreateAsync(account, secrets.Unprotect(account.PasswordProtected), ct);
        var loggedIn = false;

        foreach (var slip in pending)
        {
            if (!loggedIn) { await client.LoginAsync(ct); loggedIn = true; }
            var outcome = await client.GetOutcomeAsync(slip, ct);
            slip.ResultCheckedAt = clock.GetUtcNow().UtcDateTime;
            if (outcome == BetOutcome.Pending)
            {
                account.NextCheckAt = NextResultCheck(slip);
                return await Done(account, "Waiting",
                    $"Slip #{slip.Id} is not settled on {account.Site} yet (not lost either); next result check {EatTime(account.NextCheckAt.Value):dd/MM HH:mm} EAT.");
            }
            await SettleAsync(account, slip, outcome == BetOutcome.Won, await client.GetBalanceAsync(ct), ct);
            if (account.Stopped)
                return await Done(account, "Stopped", $"{account.LossStreak} consecutive losses. Betting stopped.");
        }

        // 2) Matches from the public pages (no login), all the account's leagues together, earliest day first: today's
        //    matches alone; if they can't make a slip, today + tomorrow; and so on up to MaxDaysAhead days. So an earlier
        //    match in any league is always used before a later one (e.g. Bundesliga on Friday before EPL on Saturday).
        var stake = StakeCalculator.NextStake(account);
        var endOfToday = TimeZoneInfo.ConvertTimeToUtc(today.AddDays(1).ToDateTime(TimeOnly.MinValue), Eat.Zone);
        var accountRules = _rules.ForAccount(account);
        // Matches already riding on open slips (all accounts of the same company) with the same market + minutes are not bet again.
        accountRules.Taken = await OpenPicks.LoadAsync(db, account.Id, nowUtc, _rules.MatchMinutes, ct);

        // A slip chosen by an earlier run that could not place it (login failed, …): continue with it while every match is
        // still to start and the stake is unchanged, instead of reading all the leagues again.
        SlipPlan? plan = null;
        if (PreparedSlips.Get(account.Id) is { } prepared)
        {
            var firstKickoff = prepared.Plan.Picks.Min(p => p.Match.Kickoff);
            var nowTaken = prepared.Plan.Picks.Select(p => SlipBuilder.TakenBy(p.Match, p.Selection, accountRules)).FirstOrDefault(t => t is not null);
            if (nowTaken is not null)
                await Log(account, $"The slip chosen at {EatTime(prepared.ChosenAtUtc):dd/MM HH:mm} EAT has {nowTaken.Home} v {nowTaken.Away}, " +
                                   $"now bet in the same minutes on {nowTaken.Where}; choosing a new slip.");
            if (nowTaken is null && prepared.Stake == stake && prepared.Reuses < PreparedSlips.MaxReuses && firstKickoff > nowUtc.AddMinutes(5))
            {
                plan = prepared.Plan;
                // Held until its placing time: no login, no try counted.
                if (PlaceAt(plan) > nowUtc.AddMinutes(1))
                    return await Hold(account, plan, prepared.ChosenAtUtc);
                PreparedSlips.Save(account.Id, prepared with { Reuses = prepared.Reuses + 1 });
                await Log(account, $"Continuing with the slip chosen at {EatTime(prepared.ChosenAtUtc):dd/MM HH:mm} EAT " +
                                   $"({plan.Picks.Count} picks @ {plan.CombinedOdds}); matches not read again.");
            }
            else PreparedSlips.Remove(account.Id);
        }
        if (plan is null)
        {
            var matches = await client.GetMatchesAsync(account.Leagues, account.Markets, endOfToday.AddDays(_rules.MaxDaysAhead - 1), ct);
            matches = SlipBuilder.InLeagues(matches, account.Leagues).Where(m => m.Kickoff > clock.GetUtcNow().UtcDateTime).ToList();
            if (!matches.Any(m => m.Selections.Any(x => accountRules.Markets.Any(c => c.Market == x.Market && c.Side == x.Side))))
                return await Done(account, "NoMatches",
                    $"No matches offering {Describe(account)} in {string.Join(", ", account.Leagues)} in the next {_rules.MaxDaysAhead} days. Next check in {_rules.CheckIntervalMinutes} min.");

            // Reading the matches takes minutes and other sites' runs go on meanwhile: one account chooses at a time, with the
            // open and chosen picks read again just before, so two accounts never take the same match + minutes together.
            await ChooseLock.WaitAsync(ct);
            try
            {
                accountRules.Taken = await OpenPicks.LoadAsync(db, account.Id, clock.GetUtcNow().UtcDateTime, _rules.MatchMinutes, ct);
                for (int d = 0; d < _rules.MaxDaysAhead && plan is null; d++)
                {
                    plan = SlipBuilder.Build(matches.Where(m => m.Kickoff < endOfToday.AddDays(d)), accountRules);
                    if (plan is not null)
                        await Log(account, d == 0 ? "Slip built from today's matches." : $"Today's matches were not enough; slip built with matches up to {today.AddDays(d):dd/MM}.");
                }
                if (plan is null)
                    return await Done(account, "NoSlip",
                        $"No combination of up to {accountRules.MaxMatches} picks at {accountRules.MinPickOdds}–{accountRules.MaxPickOdds} reaches " +
                        $"{accountRules.MinCombinedOdds}–{accountRules.MaxCombinedOdds} within {_rules.MaxDaysAhead} days in {string.Join(", ", account.Leagues)}. " +
                        $"Next check in {_rules.CheckIntervalMinutes} min.");

                // Last guard: never send two selections of one match to a site (sites refuse them in one accumulator).
                if (plan.Picks.GroupBy(p => SlipBuilder.MatchKey(p.Match)).FirstOrDefault(g => g.Count() > 1) is { } twice)
                    return await Done(account, "NotFilled", $"The slip has {twice.First().Match.Home} v {twice.First().Match.Away} twice; nothing was sent to {account.Site}.");

                // Kept until placed: if the login or the placing fails, the next run continues from here.
                PreparedSlips.Save(account.Id, new PreparedSlips.Prepared(plan, stake, nowUtc, 0));
            }
            finally { ChooseLock.Release(); }
            // Placed only shortly before the first kickoff (after the line-ups); until then the matches are kept for this account.
            if (PlaceAt(plan) > nowUtc.AddMinutes(1))
                return await Hold(account, plan, nowUtc);
        }

        // 3) A slip is possible: log in only now, for the balance and the bet.
        if (!loggedIn) { await client.LoginAsync(ct); loggedIn = true; }
        var balance = await client.GetBalanceAsync(ct);
        account.LastBalance = balance;
        if (balance <= 0)
            return await Skip(account, today, stake, balance, "Balance is zero. No bet.", ct);
        if (balance < stake)
        {
            await notifier.SendAsync($"Please top up {account.Username}",
                $"Account {account.Username} has {balance:N0} {account.Currency} but the next slip needs {stake:N0} {account.Currency} " +
                $"(loss streak {account.LossStreak}). Please update the balance; the system tries again every {_rules.CheckIntervalMinutes} minutes.", ct);
            return await Skip(account, today, stake, balance, $"Balance {balance:N0} is less than stake {stake:N0}. Top-up requested by email.", ct);
        }

        // 4) Place.
        var newSlip = new Slip
        {
            AccountId = account.Id,
            BetDay = today,
            Stake = stake,
            CombinedOdds = plan.CombinedOdds,
            PotentialReturn = Math.Round(stake * plan.CombinedOdds, 2),
            BalanceBefore = balance,
            SettlementChecks = _rules.SettlementChecks(plan.Picks.Select(p => p.Match.Kickoff)),
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
            catch (PickGoneException ex)
            {
                // A pick's odds moved out of range (or its market went): a new slip is chosen at the next try.
                PreparedSlips.Remove(account.Id);
                account.NextCheckAt = nowUtc.AddMinutes(PreparedSlips.RetryMinutes);
                return await Done(account, "NotPlaced", $"Slip not placed: {ex.Message} A new slip is chosen in {PreparedSlips.RetryMinutes} min.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The chosen slip is kept: try it again soon rather than after the full check interval.
                account.NextCheckAt = nowUtc.AddMinutes(PreparedSlips.RetryMinutes);
                return await Done(account, "NotPlaced", $"Slip not placed: {ex.Message} Trying the same slip again in {PreparedSlips.RetryMinutes} min.");
            }
            newSlip.Status = SlipStatus.Pending;
        }
        db.Slips.Add(newSlip);
        // A placed slip: nothing to do for this account until its first result check (a loss may show there already).
        if (newSlip.Status == SlipStatus.Pending && newSlip.SettlementChecks.Count > 0)
            account.NextCheckAt = newSlip.SettlementChecks[0];
        await db.SaveChangesAsync(ct);
        PreparedSlips.Remove(account.Id); // used: the next slip is chosen afresh
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
        var slip = await db.Slips.Include(s => s.Picks).SingleOrDefaultAsync(s => s.Id == slipId, ct) ?? throw new KeyNotFoundException("Slip not found.");
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
        if (won) foreach (var p in slip.Picks) p.Result = PickResult.Won; // a lost slip's losing picks are marked by the user
        account.LastBalance = balance ?? account.LastBalance;
        account.LossStreak = won ? 0 : account.LossStreak + 1;
        await Log(account, $"Slip #{slip.Id} {slip.Status}. Loss streak now {account.LossStreak}.");

        if (!won && account.LossStreak >= account.MaxLosses)
        {
            account.Stopped = true;
            await notifier.SendAsync($"STOPPED: {account.LossStreak} losses in a row ({account.Username})",
                $"Account {account.Username} lost {account.LossStreak} slips in a row. Betting is stopped.\n" +
                $"Balance: {balance:N0} {account.Currency}. Reset the account in the dashboard to resume at {account.BaseStake:N0}.", ct);
        }
        else if (!won && account.LossStreak == account.MaxLosses - 1)
        {
            await notifier.SendAsync($"{account.LossStreak} losses in a row ({account.Username})",
                $"Account {account.Username} has lost {account.LossStreak} slips in a row.\n" +
                $"Next stake: {StakeCalculator.NextStake(account):N0} {account.Currency}. Balance: {balance:N0}.\n" +
                $"Betting stops automatically after {account.MaxLosses} losses.", ct);
        }
        await db.SaveChangesAsync(ct);
        await excel.WriteFileAsync(ct);
    }

    /// <summary>The slip's result check times; slips placed before these were stored get them from their picks.</summary>
    private List<DateTime> Checks(Slip s) =>
        s.SettlementChecks.Count > 0 ? s.SettlementChecks
        : s.Picks.Count > 0 ? _rules.SettlementChecks(s.Picks.Select(p => p.Kickoff))
        : [s.CreatedAt.AddMinutes(_rules.MatchMinutes)];

    /// <summary>
    /// The result should be read now: a check time has passed since it was last read, or the last check time is over and
    /// the slip is still open (then it is read again every check interval).
    /// </summary>
    private bool ResultDue(Slip s)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var last = s.ResultCheckedAt ?? DateTime.MinValue;
        var checks = Checks(s);
        return checks.Any(t => t <= now && t > last)
            || (checks[^1] <= now && last.AddMinutes(_rules.CheckIntervalMinutes) <= now);
    }

    /// <summary>The next check time still ahead, or one check interval from now once all have passed.</summary>
    private DateTime NextResultCheck(Slip s)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return Checks(s).Where(t => t > now).DefaultIfEmpty(now.AddMinutes(_rules.CheckIntervalMinutes)).Min();
    }

    /// <summary>When a chosen slip is placed: PlaceBeforeKickoffMinutes before its first kickoff.</summary>
    private DateTime PlaceAt(SlipPlan plan) => plan.Picks.Min(p => p.Match.Kickoff).AddMinutes(-_rules.PlaceBeforeKickoffMinutes);

    /// <summary>Keeps the chosen slip (its matches stay taken for other accounts) and comes back at its placing time.</summary>
    private Task<RunResult> Hold(Account account, SlipPlan plan, DateTime chosenAtUtc)
    {
        var placeAt = PlaceAt(plan);
        account.NextCheckAt = placeAt;
        var first = plan.Picks.MinBy(p => p.Match.Kickoff)!.Match;
        return Done(account, "Holding",
            $"Slip chosen at {EatTime(chosenAtUtc):dd/MM HH:mm} EAT ({plan.Picks.Count} picks @ {plan.CombinedOdds}); first match " +
            $"{first.Home} v {first.Away} kicks off {EatTime(first.Kickoff):dd/MM HH:mm}. It is placed at {EatTime(placeAt):dd/MM HH:mm} EAT " +
            $"({_rules.PlaceBeforeKickoffMinutes} min before kickoff).");
    }

    private static DateTime EatTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Eat.Zone);

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
