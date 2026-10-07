using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mikeka.Api.Data;

namespace Mikeka.Api.Services;

/// <summary>
/// Wakes every minute and runs each active account whose NextCheckAt has come (null = now). The engine reads the open
/// slip's result at its check times, or looks for matches (no login) and logs in only to place a slip.
/// One betting company = one account at a time: the next account of that company starts SameSiteDelayMinutes after the
/// previous one finished. Different companies run side by side.
/// </summary>
public class CheckScheduler(IServiceScopeFactory scopes, IOptions<BettingRules> rules, TimeProvider clock, ILogger<CheckScheduler> log)
    : BackgroundService
{
    /// <summary>Per site: the run in progress (if any) and when the site is free again.</summary>
    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _freeAt = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        do
        {
            try { await TickAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Scheduler tick failed"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        List<(int Id, string Site)> due;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MikekaDb>();
            due = (await db.Accounts
                    .Where(a => a.IsActive && !a.Stopped && (a.NextCheckAt == null || a.NextCheckAt <= now))
                    .OrderBy(a => a.NextCheckAt ?? DateTime.MinValue).ThenBy(a => a.Id)
                    .Select(a => new { a.Id, a.Site }).ToListAsync(ct))
                .Select(a => (a.Id, a.Site)).ToList();
        }

        foreach (var group in due.GroupBy(a => a.Site, StringComparer.OrdinalIgnoreCase))
        {
            var site = group.Key;
            if (_running.TryGetValue(site, out var busy) && !busy.IsCompleted) continue;
            if (_freeAt.TryGetValue(site, out var free) && now < free) continue;
            var id = group.First().Id; // the one waiting longest; the others follow after the delay
            _running[site] = Task.Run(() => RunOneAsync(id, site, ct), ct);
        }
    }

    private async Task RunOneAsync(int accountId, string site, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<BettingEngine>().RunAsync(accountId, manual: false, ct);
            log.LogInformation("Check of account {Id} ({Site}): {Outcome} — {Message}", accountId, site, result.Outcome, result.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Run failed for account {Id}", accountId);
            var db = scope.ServiceProvider.GetRequiredService<MikekaDb>();
            db.ChangeTracker.Clear();
            db.RunLogs.Add(new Domain.RunLog { AccountId = accountId, Level = "Error", Message = ex.Message });
            // Try again after the normal interval rather than every minute.
            if (await db.Accounts.FindAsync([accountId], ct) is { } account)
                account.NextCheckAt = clock.GetUtcNow().UtcDateTime.AddMinutes(rules.Value.CheckIntervalMinutes);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _freeAt[site] = clock.GetUtcNow().UtcDateTime.AddMinutes(rules.Value.SameSiteDelayMinutes);
        }
    }
}
