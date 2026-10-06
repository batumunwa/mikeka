using Microsoft.Playwright;

namespace Mikeka.Api.Bookmaker;

/// <summary>Opening pages on the betting sites, which sometimes take over a minute to answer once and then load fine.</summary>
public static class PageLoad
{
    public const int Attempts = 3;

    /// <summary>
    /// Goes to <paramref name="url"/>; on a timeout or network error waits a few seconds and tries again, up to
    /// <see cref="Attempts"/> times. The last failure is thrown as before.
    /// </summary>
    public static async Task OpenAsync(this IPage page, string url, ILogger? log = null, WaitUntilState waitUntil = WaitUntilState.DOMContentLoaded)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await page.GotoAsync(url, new() { WaitUntil = waitUntil, Timeout = 60_000 });
                return;
            }
            catch (Exception ex) when (attempt < Attempts && (ex is TimeoutException || ex is PlaywrightException { Message: var m } && m.Contains("net::ERR_")))
            {
                log?.LogWarning("Page did not load ({Error}); try {Next} of {Attempts}: {Url}", ex.Message.Split('\n')[0], attempt + 1, Attempts, url);
                await Task.Delay(5_000);
            }
        }
    }
}
