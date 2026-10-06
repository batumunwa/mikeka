using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Mikeka.Api.Bookmaker;

/// <summary>
/// Clicks "Place" on a site whose confirmation screen has not been seen yet (Leonbet, 1win) and decides whether the bet
/// went through. Confirmed = the balance dropped by the stake, or a bet number / "bet accepted" text appeared that was not
/// on the page before the click.
/// </summary>
public static class PlaceConfirm
{
    private static readonly Regex Accepted = new(@"bet (was |has been |is )?(accepted|placed)|successfully placed", RegexOptions.IgnoreCase);
    private static readonly Regex BetNumber = new(@"(?:bet|coupon|ticket)\s*(?:id|no\.?|number|№|#)\s*:?\s*#?\s*(\d{5,})", RegexOptions.IgnoreCase);

    /// <summary>
    /// Clicks <paramref name="placeButton"/>, then waits up to <paramref name="seconds"/>. Returns the site's bet number if one
    /// appears, "{site}-{time}" if confirmed without one, or null if nothing confirmed it (the caller reports "unconfirmed").
    /// </summary>
    public static async Task<string?> ClickAndWaitAsync(IPage page, ILocator placeButton, string site, Func<Task<decimal>> readBalance,
        decimal balanceBefore, decimal stake, int seconds, ILogger log)
    {
        var before = await BodyTextAsync(page);
        var oldNumbers = BetNumber.Matches(before).Select(m => m.Groups[1].Value).ToHashSet();
        int oldAccepted = Accepted.Matches(before).Count;

        await placeButton.ClickAsync(new() { Timeout = 10_000 });
        log.LogInformation("{Site}: Place clicked", site);

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        string? why = null;
        while (DateTime.UtcNow < deadline && why is null)
        {
            await page.WaitForTimeoutAsync(2_000);
            var text = await BodyTextAsync(page);
            if (BetNumber.Matches(text).Select(m => m.Groups[1].Value).FirstOrDefault(n => !oldNumbers.Contains(n)) is { } id)
            {
                log.LogInformation("{Site}: bet number {Id} shown after Place", site, id);
                return id;
            }
            if (Accepted.Matches(text).Count > oldAccepted) why = "the site says the bet was accepted";
            else
            {
                try
                {
                    var now = await readBalance();
                    if (now <= balanceBefore - stake + 1) why = $"balance {balanceBefore:N0} → {now:N0}"; // the stake left the balance
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException or FormatException) { }
            }
        }
        if (why is null) return null;
        log.LogInformation("{Site}: bet confirmed after Place ({Why}), no bet number shown", site, why);
        return $"{site}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
    }

    private static async Task<string> BodyTextAsync(IPage page)
    {
        try { return await page.Locator("body").InnerTextAsync(new() { Timeout = 5_000 }); }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { return ""; }
    }
}
