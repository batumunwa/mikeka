using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Mikeka.Api.Domain;

namespace Mikeka.Api.Bookmaker;

/// <summary>
/// Reads a slip's result from a site's bet-history page when the site gave no bet number (Leonbet, 1win): the bet is the
/// smallest block on the page that names every pick's home and away team; its status words give the result.
/// </summary>
public static class BetHistory
{
    /// <summary>
    /// Returns the text of each smallest element holding all <c>teams</c> (compared lower-case, letters and digits only),
    /// newest-first as the page lists them. Scrolls a few times so lazily loaded rows appear.
    /// </summary>
    private const string FindCardsScript = @"async teams => {
        const norm = s => (s || '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
        const want = teams.map(norm).filter(t => t.length > 0);
        const has = el => { const t = ' ' + norm(el.innerText) + ' '; return want.every(w => t.includes(' ' + w + ' ')); };
        for (let i = 0; i < 4; i++) { window.scrollBy(0, window.innerHeight); await new Promise(r => setTimeout(r, 700)); }
        window.scrollTo(0, 0);
        const all = [...document.body.querySelectorAll('*')].filter(el => el.offsetParent !== null && has(el));
        const smallest = all.filter(el => ![...el.querySelectorAll('*')].some(c => all.includes(c)));
        return smallest.map(el => {
            // The status often sits beside the teams: climb only until a status word appears (so no neighbouring bet is taken in).
            const status = /\b(won|(?<!(possible|potential|max|to) )win|lost|lose|loss|pending|active|unsettled|not settled|in progress|open|returned|refund|cashed out)\b/i;
            let card = el;
            for (let j = 0; j < 5 && !status.test(card.innerText) && card.parentElement && card.parentElement.innerText.length < 2000; j++)
                card = card.parentElement;
            return card.innerText.replace(/\s+/g, ' ').trim();
        });
    }";

    /// <summary>"Possible win 2,100" and similar are on every open bet: removed before looking for result words.</summary>
    private static readonly Regex PayoutLabels = new(
        @"\b(possible|potential|max(imum)?|to|expected|est(imated)?)\s+(win(ning)?s?|payout|return)\b|\bwin(ning)?s?\s+amount\b",
        RegexOptions.IgnoreCase);
    private static readonly Regex LostWords = new(@"\b(lost|lose|loss|losing|lost bet)\b", RegexOptions.IgnoreCase);
    private static readonly Regex OpenWords = new(
        @"\b(pending|active|not settled|unsettled|in progress|in play|live|open|awaiting|not calculated|unresolved|running)\b",
        RegexOptions.IgnoreCase);
    private static readonly Regex WonWords = new(@"\b(won|win|winning|paid out|payout received|cashed out|returned|refund(ed)?)\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// The result written in one bet's block. Any loss word = Lost (one lost pick loses the slip, even while others play);
    /// else an "open" word = Pending; else a win word = Won; else Pending (unknown).
    /// </summary>
    public static BetOutcome Classify(string cardText)
    {
        var text = PayoutLabels.Replace(cardText, " ");
        if (LostWords.IsMatch(text)) return BetOutcome.Lost;
        if (OpenWords.IsMatch(text)) return BetOutcome.Pending;
        if (WonWords.IsMatch(text)) return BetOutcome.Won;
        return BetOutcome.Pending;
    }

    /// <summary>
    /// Opens the history page (each path in turn, then a link whose text matches <paramref name="linkRegex"/>) and reads the
    /// slip's result. Returns null when the bet is not found on the page (the caller saves a screenshot).
    /// </summary>
    public static async Task<(BetOutcome outcome, string text)?> ReadAsync(
        IPage page, Func<string, string> at, IEnumerable<string> paths, string linkRegex, Slip slip, ILogger log)
    {
        var teams = slip.Picks.SelectMany(p => new[] { p.Home, p.Away }).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToArray();
        if (teams.Length == 0) return null;

        foreach (var path in paths)
        {
            await page.OpenAsync(at(path), log);
            await page.WaitForTimeoutAsync(3_000);
            if (await FindAsync(page, teams) is { } found) return found;
        }

        // The menu link ("My bets", "Bet history", …) as a last resort.
        var link = page.GetByText(new Regex(linkRegex, RegexOptions.IgnoreCase)).First;
        if (await link.CountAsync() > 0)
        {
            try
            {
                await link.ClickAsync(new() { Timeout = 10_000 });
                await page.WaitForTimeoutAsync(4_000);
                if (await FindAsync(page, teams) is { } found) return found;
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                log.LogWarning("Bet history link click failed: {Error}", ex.Message.Split('\n')[0]);
            }
        }
        return null;
    }

    private static async Task<(BetOutcome, string)?> FindAsync(IPage page, string[] teams)
    {
        string[] cards = [];
        // The page may still redirect or reload while it is read ("Execution context was destroyed", 1win 2026-10-08): try again.
        for (int attempt = 1; ; attempt++)
        {
            try { cards = await page.EvaluateAsync<string[]>(FindCardsScript, teams); break; }
            catch (PlaywrightException ex) when (attempt < 3 && ex.Message.Contains("context was destroyed", StringComparison.OrdinalIgnoreCase))
            {
                try { await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 15_000 }); }
                catch (TimeoutException) { }
                await page.WaitForTimeoutAsync(3_000);
            }
        }
        if (cards.Length == 0) return null;
        var text = cards[0]; // newest first on the history pages
        return (Classify(text), text);
    }
}
