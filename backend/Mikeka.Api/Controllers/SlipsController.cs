using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mikeka.Api.Data;
using Mikeka.Api.Services;

namespace Mikeka.Api.Controllers;

[ApiController, Route("api")]
public class SlipsController(MikekaDb db, ExcelLog excel) : ControllerBase
{
    [HttpGet("slips")]
    public async Task<IActionResult> List(int? accountId, int take = 100) =>
        Ok(await db.Slips.AsNoTracking().Include(s => s.Picks)
            .Where(s => accountId == null || s.AccountId == accountId)
            .OrderByDescending(s => s.CreatedAt).Take(take)
            .Select(s => new
            {
                s.Id, s.AccountId, s.BetDay, s.CreatedAt, s.SettledAt, Status = s.Status.ToString(), s.Stake,
                s.CombinedOdds, s.PotentialReturn, s.BetReference, s.BalanceBefore, s.BalanceAfter, s.Note, s.SettlementChecks, s.ResultCheckedAt,
                Picks = s.Picks.OrderBy(p => p.Kickoff).Select(p => new { p.League, p.Home, p.Away, p.Kickoff, p.Market, p.Side, p.Line, p.Odds, p.Interval, p.Label }),
            })
            .ToListAsync());

    /// <summary>
    /// Deletes a slip that was never placed (Draft, Skipped/dry-run, or an unconfirmed one the user found was not placed).
    /// Placed, won or lost slips are kept.
    /// </summary>
    [HttpDelete("slips/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var slip = await db.Slips.Include(s => s.Picks).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (slip is null) return NotFound();
        if (slip.Status is not (Domain.SlipStatus.Draft or Domain.SlipStatus.Skipped or Domain.SlipStatus.Pending) || slip.BetReference is not null)
            return ValidationProblem($"Slip #{id} is {slip.Status}; only slips that were never placed can be deleted.");
        db.Slips.Remove(slip);
        db.RunLogs.Add(new Domain.RunLog { AccountId = slip.AccountId, Message = $"Slip #{id} ({slip.Status}) deleted by the user." });
        await db.SaveChangesAsync(ct);
        await excel.WriteFileAsync(ct);
        return NoContent();
    }

    /// <summary>Result entered by hand for a Pending slip (e.g. one placed without a bet number).</summary>
    [HttpPost("slips/{id:int}/settle")]
    public async Task<IActionResult> Settle(int id, bool won, [FromServices] BettingEngine engine, CancellationToken ct)
    {
        try { await engine.SettleManuallyAsync(id, won, ct); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return ValidationProblem(ex.Message); }
        return NoContent();
    }

    [HttpGet("logs")]
    public async Task<IActionResult> Logs(int? accountId, int take = 100) =>
        Ok(await db.RunLogs.AsNoTracking()
            .Where(l => accountId == null || l.AccountId == accountId)
            .OrderByDescending(l => l.Id).Take(take).ToListAsync());

    /// <summary>Excel log: date, matches, odds, stake, result and balance per slip.</summary>
    [HttpGet("slips/export")]
    public async Task<IActionResult> Export(int? accountId, CancellationToken ct) =>
        File(await excel.BuildAsync(accountId, ct),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"mikeka-slips-{DateTime.UtcNow:yyyyMMdd}.xlsx");
}

[ApiController, Route("api/email")]
public class EmailController(INotifier notifier, Microsoft.Extensions.Options.IOptions<EmailOptions> options) : ControllerBase
{
    /// <summary>Sends a test message to the alert address so you can confirm alerts arrive.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        await notifier.SendAsync("Test email", "Mikeka alerts are working. 3-loss alerts, the 4-loss stop and top-up requests will arrive at this address.", ct);
        return Ok(new { sentTo = options.Value.To });
    }
}

[ApiController, Route("api/accounts/{accountId:int}/analysis")]
public class AnalysisController(SlipAnalysis analysis) : ControllerBase
{
    /// <summary>The default window (EAT), e.g. today 10:00 to tomorrow 09:59, for pre-filling the form.</summary>
    [HttpGet("window")]
    public IActionResult Window()
    {
        var (from, to) = analysis.DefaultWindow();
        return Ok(new { from = from.ToString("yyyy-MM-ddTHH:mm"), to = to.ToString("yyyy-MM-ddTHH:mm") });
    }

    /// <summary>Saves the slip proposed by this account's last analysis as a Draft (not placed with the betting company).</summary>
    [HttpPost("create-slip")]
    public async Task<IActionResult> CreateSlip(int accountId, CancellationToken ct)
    {
        try
        {
            var slip = await analysis.CreateDraftAsync(accountId, ct);
            return Ok(new { slip.Id, slip.Stake, slip.CombinedOdds, picks = slip.Picks.Count });
        }
        catch (InvalidOperationException ex) { return ValidationProblem(ex.Message); }
    }

    /// <summary>Analysis of the window (EAT times, "yyyy-MM-ddTHH:mm") from the public site, no login. Never places a bet.</summary>
    [HttpPost]
    public async Task<IActionResult> Run(int accountId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (defFrom, defTo) = analysis.DefaultWindow();
        var f = from ?? defFrom;
        var t = to ?? defTo;
        if (t <= f) return ValidationProblem("The end of the window must be after its start.");
        try
        {
            return Ok(await analysis.AnalyseAsync(accountId, f, t, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not KeyNotFoundException)
        {
            return Ok(new { error = ex.Message });
        }
    }
}

[ApiController, Route("api/run")]
public class RunController(BettingEngine engine, MikekaDb db) : ControllerBase
{
    /// <summary>"Run now" for one account (also retries a skipped day, e.g. after a top-up). A failure is written to the account's Activity.</summary>
    [HttpPost("{accountId:int}")]
    public async Task<RunResult> Run(int accountId, CancellationToken ct)
    {
        try
        {
            return await engine.RunAsync(accountId, manual: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = "Run failed: " + ex.Message.Split('\n')[0];
            db.RunLogs.Add(new Domain.RunLog { AccountId = accountId, Level = "Error", Message = message });
            await db.SaveChangesAsync(CancellationToken.None);
            return new RunResult(accountId, "Failed", message);
        }
    }
}
