using Microsoft.EntityFrameworkCore;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;

namespace Mikeka.Api.Services;

/// <summary>
/// Picks that are still riding on the same betting company (user's rule 2026-10-08, made per company 2026-10-10: "each
/// company has its own market"): on one company the same match is never bet twice at the same time with the same market
/// family and minute range; another company may take it. Open = on a Pending slip, or on a slip another account of that
/// company has chosen but not placed yet (PreparedSlips), as long as the match is not over.
/// </summary>
public static class OpenPicks
{
    public static async Task<List<TakenPick>> LoadAsync(MikekaDb db, int exceptAccountId, DateTime nowUtc, int matchMinutes, CancellationToken ct)
    {
        var since = nowUtc.AddMinutes(-matchMinutes);
        var sites = await db.Accounts.Select(a => new { a.Id, a.Site }).ToDictionaryAsync(a => a.Id, a => a.Site.ToLower(), ct);
        var site = sites.GetValueOrDefault(exceptAccountId) ?? "";

        var rows = await db.Slips.Where(s => s.Status == SlipStatus.Pending && s.Account!.Site.ToLower() == site)
            .SelectMany(s => s.Picks.Where(p => p.Kickoff > since),
                (s, p) => new { p.Home, p.Away, p.Kickoff, p.Market, p.Interval, p.SlipId, s.Account!.Site, User = s.Account.Username })
            .ToListAsync(ct);
        var taken = rows.Select(r => new TakenPick(r.Home, r.Away, r.Kickoff, SlipBuilder.Family(r.Market), SlipBuilder.Range(r.Interval),
            $"slip #{r.SlipId} ({r.User}, {r.Site})")).ToList();

        foreach (var (accountId, prepared) in PreparedSlips.All())
        {
            if (accountId == exceptAccountId || sites.GetValueOrDefault(accountId) != site) continue;
            taken.AddRange(prepared.Plan.Picks.Where(p => p.Match.Kickoff > since).Select(p => new TakenPick(
                p.Match.Home, p.Match.Away, p.Match.Kickoff, SlipBuilder.Family(p.Selection.Market), SlipBuilder.Range(p.Selection.Interval),
                $"the slip chosen for account {accountId} (not placed yet)")));
        }
        return taken;
    }
}
