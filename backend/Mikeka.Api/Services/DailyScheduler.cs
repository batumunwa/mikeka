using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mikeka.Api.Data;

namespace Mikeka.Api.Services;

/// <summary>
/// Wakes every TickMinutes. From RunAt (08:00 EAT) each active account gets a run; the engine itself
/// decides whether to wait for an unsettled slip, skip, or place. Unsettled slips are re-checked each tick.
/// </summary>
public class DailyScheduler(IServiceScopeFactory scopes, IOptions<BettingRules> rules, TimeProvider clock, ILogger<DailyScheduler> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(rules.Value.TickMinutes), clock);
        do
        {
            try { await TickAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Scheduler tick failed"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var beforeRunAt = TimeOnly.FromDateTime(Eat.Now(clock)) < rules.Value.RunAt;
        if (beforeRunAt)
        {
            // Before 08:00 only settle slips that are waiting for a result.
            using var s = scopes.CreateScope();
            var db0 = s.ServiceProvider.GetRequiredService<MikekaDb>();
            if (!await db0.Slips.AnyAsync(x => x.Status == Domain.SlipStatus.Pending, ct)) return;
        }

        List<(int id, string site)> accounts;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MikekaDb>();
            accounts = (await db.Accounts.Where(a => a.IsActive && !a.Stopped).Select(a => new { a.Id, a.Site }).ToListAsync(ct))
                .Select(a => (a.Id, a.Site)).ToList();
        }

        foreach (var (id, site) in accounts)
        {
            using var scope = scopes.CreateScope();
            try
            {
                // Every site: automatic selection, saved as a Draft and emailed; the user places the bets.
                if (beforeRunAt) continue;
                var sp = scope.ServiceProvider;
                var result = await sp.GetRequiredService<SlipAnalysis>().DailySelectionAsync(id, sp.GetRequiredService<INotifier>(), ct);
                log.LogInformation("Daily selection for account {Id} ({Site}): {Result}", id, site, result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Run failed for account {Id}", id);
                var db = scope.ServiceProvider.GetRequiredService<MikekaDb>();
                db.ChangeTracker.Clear();
                db.RunLogs.Add(new Domain.RunLog { AccountId = id, Level = "Error", Message = ex.Message });
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
