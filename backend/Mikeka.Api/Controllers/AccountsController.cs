using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Api.Controllers;

public record AccountDto(int Id, string Name, string Url, string Site, string Username, string Currency, bool IsActive,
    List<string> Leagues, List<MarketChoice> Markets, int LossStreak, bool Stopped, decimal? LastBalance, decimal NextStake, decimal BaseStake,
    int MaxLosses, decimal MinPickOdds, decimal MaxPickOdds, decimal MinCombinedOdds, decimal MaxCombinedOdds,
    DateTime? NextCheckAt, int MaxPicks, bool BettingPaused);

public record SaveAccountRequest(
    [Required] string Name,
    [Required, Url] string Url,
    [Required] string Username,
    /// <summary>Required when creating; leave empty on update to keep the stored password.</summary>
    string? Password,
    /// <summary>Markets (goals/corners/cards/fouls), always Under, each with its leagues. At least one.</summary>
    [Required, MinLength(1)] List<MarketChoice> Markets,
    string Currency = "TZS",
    bool IsActive = true,
    /// <summary>Normal stake; doubled after each loss, back to this after a win. Empty = suggested value from config.</summary>
    decimal? BaseStake = null,
    /// <summary>"coldbet" or "1win". Empty = guessed from the URL.</summary>
    string? Site = null,
    /// <summary>Losses in a row that stop betting. Empty = the default from Settings.</summary>
    int? MaxLosses = null,
    /// <summary>This account's odds ranges. Empty = the defaults from Settings.</summary>
    decimal? MinPickOdds = null, decimal? MaxPickOdds = null, decimal? MinCombinedOdds = null, decimal? MaxCombinedOdds = null,
    /// <summary>Most picks one slip may hold. Empty = Betting:MaxMatches (6).</summary>
    int? MaxPicks = null);

[ApiController, Route("api/accounts")]
public class AccountsController(MikekaDb db, AccountSecrets secrets, Microsoft.Extensions.Options.IOptions<BettingRules> rules) : ControllerBase
{
    private AccountDto ToDto(Account a) => new(a.Id, a.Name, a.Url, a.Site, a.Username, a.Currency, a.IsActive,
        a.Leagues, a.Markets, a.LossStreak, a.Stopped, a.LastBalance, StakeCalculator.NextStake(a), a.BaseStake,
        a.MaxLosses, a.MinPickOdds, a.MaxPickOdds, a.MinCombinedOdds, a.MaxCombinedOdds, a.NextCheckAt, a.MaxPicks, a.BettingPaused);

    [HttpGet]
    public async Task<IEnumerable<AccountDto>> List() =>
        (await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync()).Select(ToDto);

    [HttpPost]
    public async Task<ActionResult<AccountDto>> Create(SaveAccountRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Password)) return ValidationProblem("Password is required.");
        if (IntervalError(req.Markets) is { } intervalError) return ValidationProblem(intervalError);
        if (req.BaseStake is <= 0) return ValidationProblem("Base stake must be more than 0.");
        var r = rules.Value; // current Settings values are the defaults
        var odds = (req.MinPickOdds ?? r.MinPickOdds, req.MaxPickOdds ?? r.MaxPickOdds, req.MinCombinedOdds ?? r.MinCombinedOdds, req.MaxCombinedOdds ?? r.MaxCombinedOdds);
        var maxPicks = req.MaxPicks ?? r.MaxMatches;
        var maxLosses = req.MaxLosses ?? r.StopAfterLosses;
        if (LimitsError(odds, maxLosses, maxPicks) is { } limitsError) return ValidationProblem(limitsError);
        var site = string.IsNullOrWhiteSpace(req.Site) ? Bookmaker.SiteBookmakerFactory.GuessSite(req.Url) : req.Site.Trim().ToLowerInvariant();
        if (!Bookmaker.SiteBookmakerFactory.Sites.Contains(site)) return ValidationProblem($"Site must be one of: {string.Join(", ", Bookmaker.SiteBookmakerFactory.Sites)}.");
        var markets = CleanMarkets(req.Markets);
        if (markets.Count == 0) return ValidationProblem("Choose at least one market: goals, corners, yellow cards or fouls.");
        if (markets.FirstOrDefault(m => m.Leagues.Count == 0) is { } noLeague)
            return ValidationProblem($"Add at least one league to the {noLeague.Market} market.");
        var leagues = markets.SelectMany(m => m.Leagues).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (await db.Accounts.AnyAsync(a => a.Url == req.Url && a.Username == req.Username))
            return Conflict("This username is already registered for that site.");
        // First check: now + check interval + the delay for each account already on this company.
        var sameSite = await db.Accounts.CountAsync(x => x.IsActive && x.Site == site);
        var firstCheck = DateTime.UtcNow.AddMinutes(r.CheckIntervalMinutes + sameSite * r.SameSiteDelayMinutes);
        var a = new Account
        {
            Name = req.Name, Url = req.Url, Username = req.Username, Currency = req.Currency, IsActive = req.IsActive,
            Leagues = leagues,
            Markets = markets,
            BaseStake = req.BaseStake ?? rules.Value.BaseStake,
            MaxLosses = maxLosses,
            MaxPicks = maxPicks,
            NextCheckAt = firstCheck,
            MinPickOdds = odds.Item1, MaxPickOdds = odds.Item2, MinCombinedOdds = odds.Item3, MaxCombinedOdds = odds.Item4,
            Site = site,
            PasswordProtected = secrets.Protect(req.Password),
        };
        db.Accounts.Add(a);
        await db.SaveChangesAsync();
        return ToDto(a);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AccountDto>> Update(int id, SaveAccountRequest req)
    {
        var a = await db.Accounts.FindAsync(id);
        if (a is null) return NotFound();
        if (IntervalError(req.Markets) is { } intervalError) return ValidationProblem(intervalError);
        if (req.BaseStake is <= 0) return ValidationProblem("Base stake must be more than 0.");
        var odds = (req.MinPickOdds ?? a.MinPickOdds, req.MaxPickOdds ?? a.MaxPickOdds, req.MinCombinedOdds ?? a.MinCombinedOdds, req.MaxCombinedOdds ?? a.MaxCombinedOdds);
        var maxPicks = req.MaxPicks ?? a.MaxPicks;
        var maxLosses = req.MaxLosses ?? a.MaxLosses;
        if (LimitsError(odds, maxLosses, maxPicks) is { } limitsError) return ValidationProblem(limitsError);
        var site = string.IsNullOrWhiteSpace(req.Site) ? Bookmaker.SiteBookmakerFactory.GuessSite(req.Url) : req.Site.Trim().ToLowerInvariant();
        if (!Bookmaker.SiteBookmakerFactory.Sites.Contains(site)) return ValidationProblem($"Site must be one of: {string.Join(", ", Bookmaker.SiteBookmakerFactory.Sites)}.");
        var markets = CleanMarkets(req.Markets);
        if (markets.Count == 0) return ValidationProblem("Choose at least one market: goals, corners, yellow cards or fouls.");
        if (markets.FirstOrDefault(m => m.Leagues.Count == 0) is { } noLeague)
            return ValidationProblem($"Add at least one league to the {noLeague.Market} market.");
        var leagues = markets.SelectMany(m => m.Leagues).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        a.Markets = markets;
        (a.MinPickOdds, a.MaxPickOdds, a.MinCombinedOdds, a.MaxCombinedOdds) = odds;
        a.MaxPicks = maxPicks;
        if (maxLosses != a.MaxLosses)
        {
            a.MaxLosses = maxLosses;
            // Raising the limit above the current streak lets a stopped account bet again; lowering it may stop it now.
            a.Stopped = a.LossStreak >= maxLosses;
        }
        a.Site = site;
        if (req.BaseStake is { } stake) a.BaseStake = stake;
        (a.Name, a.Url, a.Username, a.Currency, a.IsActive, a.Leagues) = (req.Name, req.Url, req.Username, req.Currency, req.IsActive, leagues);
        if (!string.IsNullOrWhiteSpace(req.Password)) a.PasswordProtected = secrets.Protect(req.Password);
        await db.SaveChangesAsync();
        return ToDto(a);
    }

    /// <summary>
    /// The saved password, decrypted, for checking it in the Edit form (requested by the user "for now").
    /// Only reachable from this computer (localhost); each read is written to the activity log.
    /// </summary>
    [HttpGet("{id:int}/password")]
    public async Task<IActionResult> Password(int id)
    {
        var a = await db.Accounts.FindAsync(id);
        if (a is null) return NotFound();
        db.RunLogs.Add(new RunLog { AccountId = id, Message = $"Password viewed for {a.Name} in the Edit form." });
        await db.SaveChangesAsync();
        return Ok(new { password = secrets.Unprotect(a.PasswordProtected) });
    }

    /// <summary>Resume after the 4-loss stop: clears the streak so the next slip is back at the base stake.</summary>
    [HttpPost("{id:int}/reset")]
    public async Task<ActionResult<AccountDto>> Reset(int id)
    {
        var a = await db.Accounts.FindAsync(id);
        if (a is null) return NotFound();
        a.Stopped = false;
        a.LossStreak = 0;
        db.RunLogs.Add(new RunLog { AccountId = id, Message = $"Account reset by user; stake back to {a.BaseStake:N0} {a.Currency}." });
        await db.SaveChangesAsync();
        return ToDto(a);
    }

    /// <summary>"Stop betting" (paused = true) / "Resume betting" by hand. Paused: no new slips; open slips are still read.</summary>
    [HttpPost("{id:int}/betting")]
    public async Task<ActionResult<AccountDto>> SetBetting(int id, [FromQuery] bool paused)
    {
        var a = await db.Accounts.FindAsync(id);
        if (a is null) return NotFound();
        if (a.BettingPaused == paused) return ToDto(a);
        a.BettingPaused = paused;
        if (paused) PreparedSlips.Remove(id); // a chosen slip not placed yet is dropped
        else a.NextCheckAt = null; // resumed: look for a slip at once
        db.RunLogs.Add(new RunLog { AccountId = id, Message = paused
            ? $"Betting stopped by user for {a.Name}; open slips are still checked."
            : $"Betting resumed by user for {a.Name}." });
        await db.SaveChangesAsync();
        return ToDto(a);
    }

    /// <summary>
    /// Overall balance over time, oldest first: after each recorded change, the sum of every account's latest balance.
    /// </summary>
    [HttpGet("balance-history")]
    public async Task<IActionResult> TotalBalanceHistory(CancellationToken ct)
    {
        var entries = await db.BalanceHistory.AsNoTracking().OrderBy(b => b.At).ThenBy(b => b.Id)
            .Select(b => new { b.AccountId, b.At, b.Balance }).ToListAsync(ct);
        var latest = new Dictionary<int, decimal>();
        var points = new List<object>();
        foreach (var e in entries)
        {
            latest[e.AccountId] = e.Balance;
            points.Add(new { e.At, Balance = latest.Values.Sum(), e.AccountId });
        }
        return Ok(points);
    }

    /// <summary>The account's balance over time, oldest first.</summary>
    [HttpGet("{id:int}/balance-history")]
    public async Task<IActionResult> BalanceHistory(int id, CancellationToken ct) =>
        Ok(await db.BalanceHistory.AsNoTracking().Where(b => b.AccountId == id).OrderBy(b => b.At)
            .Select(b => new { b.At, b.Balance }).ToListAsync(ct));

    /// <summary>Safe connection test: logs in and reads the balance. Never builds or places a slip.</summary>
    [HttpPost("{id:int}/check")]
    public async Task<IActionResult> Check(int id, [FromServices] Bookmaker.IBookmakerFactory bookmakers, CancellationToken ct)
    {
        var a = await db.Accounts.FindAsync([id], ct);
        if (a is null) return NotFound();
        try
        {
            await using var client = await bookmakers.CreateAsync(a, secrets.Unprotect(a.PasswordProtected), ct);
            await client.LoginAsync(ct);
            var balance = await client.GetBalanceAsync(ct);
            a.LastBalance = balance;
            db.RunLogs.Add(new RunLog { AccountId = id, Message = $"Balance checked: {balance:N2} {a.Currency}." });
            await db.SaveChangesAsync(ct);
            return Ok(new { ok = true, balance, message = $"Logged in. Balance {balance:N2} {a.Currency}." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.RunLogs.Add(new RunLog { AccountId = id, Level = "Error", Message = "Check balance failed: " + ex.Message });
            await db.SaveChangesAsync(ct);
            return Ok(new { ok = false, balance = (decimal?)null, message = ex.Message });
        }
    }

    /// <summary>Read-only, no login (public pages): the account's leagues and their odds. Never touches the bet slip.</summary>
    [HttpPost("{id:int}/matches")]
    public async Task<IActionResult> Matches(int id, [FromServices] Bookmaker.IBookmakerFactory bookmakers, [FromServices] TimeProvider clock, CancellationToken ct)
    {
        var a = await db.Accounts.FindAsync([id], ct);
        if (a is null) return NotFound();
        try
        {
            await using var client = await bookmakers.CreateAsync(a, secrets.Unprotect(a.PasswordProtected), ct);
            var until = clock.GetUtcNow().UtcDateTime.AddDays(rules.Value.MaxDaysAhead);
            var matches = await client.GetMatchesAsync(a.Leagues, a.Markets, until, ct);
            var accountRules = rules.Value.ForAccount(a);
            var withMarkets = matches.Count(m => m.Selections.Count > 0);
            db.RunLogs.Add(new RunLog { AccountId = id, Message = $"Read matches: {matches.Count} listed, {withMarkets} with total cards/corners." });
            await db.SaveChangesAsync(ct);
            return Ok(new
            {
                ok = true,
                message = $"{matches.Count} matches found, {withMarkets} with total cards/corners markets.",
                matches = matches.OrderBy(m => m.Kickoff).Select(m => new
                {
                    m.League, m.Home, m.Away, m.Kickoff, url = m.Id,
                    selections = m.Selections.OrderBy(s => s.Market).ThenBy(s => s.Line).Select(s => new { s.Market, s.Side, s.Line, s.Odds, s.Interval }),
                    pick = SlipBuilder.BestPick(m, accountRules) is { } p ? new { p.Selection.Market, p.Selection.Side, p.Selection.Line, p.Selection.Odds, p.Selection.Interval } : null,
                }),
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.RunLogs.Add(new RunLog { AccountId = id, Level = "Error", Message = "Read matches failed: " + ex.Message });
            await db.SaveChangesAsync(ct);
            return Ok(new { ok = false, message = ex.Message, matches = Array.Empty<object>() });
        }
    }

    /// <summary>
    /// Diagnostic, read-only: logs in, opens <paramref name="url"/> (a match page), picks the dropdown <paramref name="option"/>
    /// and filter <paramref name="tab"/>, returns the markets text, logs out. Used to learn the site's market titles.
    /// </summary>
    [HttpPost("{id:int}/describe-match")]
    public async Task<IActionResult> DescribeMatch(int id, string url, string option = "Regular time", string? tab = null,
        [FromServices] Bookmaker.IBookmakerFactory bookmakers = null!, CancellationToken ct = default)
    {
        var a = await db.Accounts.FindAsync([id], ct);
        if (a is null) return NotFound();
        await using var client = await bookmakers.CreateAsync(a, secrets.Unprotect(a.PasswordProtected), ct);
        if (client is not Bookmaker.ColdbetClient coldbet) return BadRequest("Only available with the Coldbet bookmaker.");
        await coldbet.LoginAsync(ct);
        return Content(await coldbet.DescribeMatchPageAsync(url, option, tab), "text/plain");
    }

    private List<MarketChoice> CleanMarkets(IEnumerable<MarketChoice>? markets) =>
        (markets ?? [])
            .Select(m => new MarketChoice
            {
                Market = (m.Market ?? "").Trim().ToLowerInvariant(),
                Side = (m.Market ?? "").Trim().Equals("result", StringComparison.OrdinalIgnoreCase) ? "Draw" : "Under", // totals are always Under
                Leagues = CleanLeagues(m.Leagues),
                Line = m.Line,
                IntervalFrom = m.IntervalFrom,
                IntervalTo = m.IntervalTo,
            })
            .Where(m => rules.Value.SupportedMarkets.Contains(m.Market) && m.Side.Length > 0)
            .DistinctBy(m => (m.Market, m.Side, m.IntervalKey, m.Line, string.Join("|", m.Leagues.Order(StringComparer.OrdinalIgnoreCase)).ToLowerInvariant()))
            .ToList();

    private static string? LimitsError((decimal minPick, decimal maxPick, decimal minCombined, decimal maxCombined) o, int maxLosses, int maxPicks) =>
        maxLosses < 1 ? "Maximum losses must be at least 1."
        : maxPicks is < 1 or > 20 ? "Maximum picks per slip must be between 1 and 20."
        : BettingRules.OddsError(o.minPick, o.maxPick, o.minCombined, o.maxCombined, maxPicks);

    /// <summary>An interval needs both start and end, start before end, within minutes 1–90.</summary>
    private static string? IntervalError(IEnumerable<MarketChoice>? markets)
    {
        foreach (var m in markets ?? [])
        {
            if ((m.Market ?? "").Trim().Equals("result", StringComparison.OrdinalIgnoreCase)
                && (m.IntervalFrom != 1 || m.IntervalTo is null))
                return "Result: Draw needs a time interval starting at minute 1, e.g. From 1 to 10.";
            if (m.Line is { } l && (l <= 0 || l > 200)) return $"{m.Market}: the Under value must be more than 0 (e.g. 0.5, 2.5, 9.5).";
            if (m.IntervalFrom is null && m.IntervalTo is null) continue;
            if (m.IntervalFrom is not { } f || m.IntervalTo is not { } t) return $"{m.Market}: choose both a start and an end minute, or neither.";
            if (f < 1 || t > 90 || f >= t) return $"{m.Market}: the interval must run from a start minute to a later end minute, within 1–90.";
        }
        return null;
    }

    /// <summary>One league per entry: "A. X, B. Y" typed or pasted as one entry is split on commas.</summary>
    private static List<string> CleanLeagues(IEnumerable<string>? leagues) =>
        (leagues ?? []).SelectMany(l => l.Split(',', ';'))
            .Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await db.Accounts.FindAsync(id);
        if (a is null) return NotFound();
        a.IsActive = false; // keep history; just stop betting
        await db.SaveChangesAsync();
        return NoContent();
    }
}
