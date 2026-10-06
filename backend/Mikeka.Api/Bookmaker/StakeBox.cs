using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Mikeka.Api.Bookmaker;

/// <summary>Types the system's stake into a site's bet-slip stake box (never clicks Place).</summary>
public static class StakeBox
{
    /// <summary>
    /// Clears the stake box and types <paramref name="stake"/> key by key, like a person; then checks the box shows it.
    /// Returns false (and logs why) if it could not: the picks stay in the slip and the user types the stake.
    /// </summary>
    public static async Task<bool> TypeAsync(IPage page, string selector, decimal stake, ILogger log, string site)
    {
        var text = stake.ToString("0.##", CultureInfo.InvariantCulture);
        try
        {
            var input = page.Locator(selector).Locator("visible=true").First;
            await input.ScrollIntoViewIfNeededAsync(new() { Timeout = 10_000 });
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                await input.ClickAsync(new() { Timeout = 10_000 });
                await input.PressAsync("Control+A");
                await input.PressAsync("Backspace");
                if (attempt == 1) await input.PressSequentiallyAsync(text, new() { Delay = 60 });
                else await input.FillAsync(text); // second try: set the value directly
                await page.WaitForTimeoutAsync(800);
                var shown = Regex.Replace(await input.InputValueAsync(), @"[^\d.]", "");
                if (decimal.TryParse(shown, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v == stake)
                {
                    log.LogInformation("{Site}: stake {Stake} typed into the bet slip", site, text);
                    return true;
                }
                log.LogWarning("{Site}: stake box shows '{Shown}' instead of {Stake} (try {Try})", site, shown, text, attempt);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            log.LogWarning("{Site}: could not type the stake {Stake}: {Error}", site, text, ex.Message.Split('\n')[0]);
        }
        return false;
    }
}
