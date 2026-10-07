using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Api.Bookmaker;

/// <summary>
/// Page settings for leonbet.co.tz ("Leonbet" section in appsettings.json). Login fields seen on the live site on 2026-10-05:
/// a phone number (+255, 9 digits) and a password on /login.
/// </summary>
public class LeonbetOptions
{
    public string DebugDir { get; set; } = "logs/leonbet-debug";
    /// <summary>Bet-history pages tried in turn to read a slip's result (not seen on the live site yet: check the first run).</summary>
    public string[] HistoryPaths { get; set; } = ["/profile/history", "/profile/bets-history", "/profile/bets"];
    /// <summary>Menu link to the bet history, clicked when no path shows the bet.</summary>
    public string HistoryLinkRegex { get; set; } = @"^\s*(my bets|bet history|bets history|betting history|history)\s*$";
    /// <summary>Lists every football league with a link, e.g. "Argentina - Super League 14".</summary>
    public string FootballPath { get; set; } = "/bets/soccer";
    /// <summary>League links on that page: /bets/soccer/{country}/{id}-{slug}.</summary>
    public string LeagueLink { get; set; } = "a[href^='/bets/soccer/'][href*='/1']";
    public string AllMarketsTab { get; set; } = "All markets";
    public string IntervalsTab { get; set; } = "Intervals";
    /// <summary>Market block titles on a match page (seen on the live site on 2026-10-05).</summary>
    public LeonbetMarket[] Markets { get; set; } =
    [
        // Over/Under block, or the Yes/No block whose "No" means no goal from kickoff to minute {to}.
        new() { Market = "goals", Title = "Total", IntervalTitles = ["Total After {to} Minutes", "Goal Scored In The First {to} Minutes"] },
        new() { Market = "result", IntervalTitles = ["{to} Minute Result", "{to} Minute Result - (1 To {to})"] },
        new() { Market = "corners", Title = "Total Corners" },
        new() { Market = "cards", Title = "Total Cards" },
        new() { Market = "fouls", Title = "Total Fouls" }, // not seen on the site yet
    ];
    public string LoginPath { get; set; } = "/login";
    public string PhoneInput { get; set; } = "input[name='login']";
    public string PasswordInput { get; set; } = "input[name='password']";
    public string LoginSubmit { get; set; } = "button[data-test-el='modal-button']";
    /// <summary>Header "Log in" link, shown only when logged out.</summary>
    public string HeaderLogin { get; set; } = "header >> text=/^\\s*Log in\\s*$/i";
    /// <summary>The bet slip's stake box (it shows 10 TZS by default).</summary>
    public string StakeInput { get; set; } = "input[id^='stake-input']";
    /// <summary>The bet slip's "Place bet" button (data-test-attr-mode="ready_to_place_bet" when it can be pressed).</summary>
    public string PlaceButton { get; set; } = "button[data-test-el='bet-slip-button_summary'][data-test-attr-mode='ready_to_place_bet']";
    /// <summary>How long to wait after "Place bet" for the bet to be confirmed.</summary>
    public int ConfirmationWaitSeconds { get; set; } = 30;
    /// <summary>A reCAPTCHA challenge frame; if it appears the system stops — it never solves challenges.</summary>
    public string CaptchaChallenge { get; set; } = "iframe[title*='challenge' i], iframe[src*='recaptcha/api2/bframe']";
    /// <summary>Text in the header that holds the balance, e.g. "TZS 32,000" or "32 000 TZS".</summary>
    public string BalanceRegex { get; set; } = @"(?:TZS\s*([\d][\d\s,.]*)|([\d][\d\s,.]*)\s*TZS)";
    /// <summary>The balance box in the top bar ("TZS 0.00").</summary>
    public string Balance { get; set; } = "[class*='balance__text']";
    /// <summary>Account menu button in the top bar; its menu holds the log-out item.</summary>
    public string ProfileButton { get; set; } = "[data-test-el^='header-profile']";
    public string LogoutRegex { get; set; } = @"^\s*(log ?out|sign ?out|exit)\s*$";
    // Log out lives at the bottom of Settings and asks "Log out?" (seen 2026-10-06).
    public string SettingsPath { get; set; } = "/profile/settings";
    public string LogoutButton { get; set; } = "[data-test-el='logout-button']";
    public string LogoutConfirm { get; set; } = "[data-test-el='logout-confirm-logout']";
}

public class LeonbetMarket
{
    public string Market { get; set; } = "";
    /// <summary>Whole-match block title (empty = none), e.g. "Total Corners".</summary>
    public string Title { get; set; } = "";
    /// <summary>
    /// Interval block titles on the match's "Intervals" tab, "{to}" replaced by the end minute (intervals start at minute 1),
    /// e.g. "Total After {to} Minutes" (Under 0.5 = no goal) or "{to} Minute Result" (X = draw). Several may be listed.
    /// </summary>
    public string[] IntervalTitles { get; set; } = [];
}

public class LeonbetFactory(IOptions<LeonbetOptions> options, SharedBrowser browser, IOptions<BettingRules> rules, ILogger<LeonbetClient> log)
{
    public async Task<IBookmakerClient> CreateAsync(Account account, string password, CancellationToken ct)
    {
        var tab = await browser.OpenTabAsync(account, options.Value.HeaderLogin, ct);
        return new LeonbetClient(tab, account, password, options.Value, rules.Value.ForAccount(account), log);
    }
}

public sealed class LeonbetClient(
    BrowserTab browserTab, Account account, string password,
    LeonbetOptions o, BettingRules rules, ILogger log) : IBookmakerClient
{
    private readonly Uri _origin = new(new Uri(account.Url).GetLeftPart(UriPartial.Authority));
    private IPage? _page;
    private bool _loggedIn;
    private Task<IPage> Page() => Task.FromResult(_page ??= browserTab.Page);
    private string At(string path) => new Uri(_origin, path).ToString();

    public async Task LoginAsync(CancellationToken ct)
    {
        var page = await Page();
        await Step("open login", () => page.OpenAsync(At(o.LoginPath), log));
        await Step("login", async () =>
        {
            // The form already shows +255: enter the remaining 9 digits.
            var digits = Regex.Replace(account.Username, @"\D", "");
            if (digits.StartsWith("255")) digits = digits[3..];
            else if (digits.StartsWith("0")) digits = digits[1..];
            await page.Locator(o.PhoneInput).First.FillAsync(digits, new() { Timeout = 20_000 });
            await page.Locator(o.PasswordInput).First.FillAsync(password, new() { Timeout = 15_000 });
            await page.Locator(o.LoginSubmit).First.ClickAsync(new() { Timeout = 15_000 });

            for (int waited = 0; waited < 60; waited += 2)
            {
                await page.WaitForTimeoutAsync(2_000);
                if (await page.Locator(o.CaptchaChallenge).First.IsVisibleAsync())
                {
                    await SaveDebug(page, "captcha", fullPage: false);
                    throw new InvalidOperationException("Leonbet showed a reCAPTCHA check. The system does not solve or bypass these, so it cannot log in on its own.");
                }
                if (!page.Url.Contains(o.LoginPath) && !await page.Locator(o.HeaderLogin).First.IsVisibleAsync())
                {
                    _loggedIn = true;
                    break;
                }
            }
            await SaveDebug(page, _loggedIn ? "logged-in" : "after-login", fullPage: false);
            if (!_loggedIn) throw new TimeoutException("Still logged out 60 s after submitting the login form (see after-login screenshot).");
        });
        log.LogInformation("Logged in to {Site} as {User}", _origin.Host, account.Username);
    }

    public async Task<decimal> GetBalanceAsync(CancellationToken ct)
    {
        var page = await Page();
        var text = await page.Locator(o.Balance).First.InnerTextAsync(new() { Timeout = 15_000 });
        var m = Regex.Match(text.Replace(' ', ' '), o.BalanceRegex, RegexOptions.IgnoreCase);
        var raw = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
        if (raw is null)
        {
            await SaveDebug(page, "balance", fullPage: false);
            throw new InvalidOperationException("Could not find the balance in Leonbet's header (see balance screenshot).");
        }
        var cleaned = Regex.Replace(raw, @"[\s,]", "");
        return decimal.Parse(cleaned, CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<MatchInfo>> GetMatchesAsync(IReadOnlyCollection<string> leagues, IReadOnlyList<MarketChoice> markets, DateTime untilUtc, CancellationToken ct)
    {
        var page = await Page();
        var listed = new List<(string url, string league, string home, string away, DateTime kickoff)>();

        // 1) League pages from /bets/soccer, by the names registered on the account ("Argentina. Super League").
        await Step("open football", () => page.OpenAsync(At(o.FootballPath), log));
        await page.Locator(o.LeagueLink).First.WaitForAsync(new() { Timeout = 30_000 });
        var leagueLinks = await page.EvaluateAsync<string[][]>(
            "sel => [...document.querySelectorAll(sel)].map(a => [a.getAttribute('href'), a.innerText.replace(/\\s+/g, ' ').trim()])", o.LeagueLink);

        foreach (var league in leagues)
        {
            ct.ThrowIfCancellationRequested();
            var href = FindLeague(leagueLinks, league);
            if (href is null) { log.LogWarning("Leonbet: league '{League}' not listed (no matches now, or the name differs)", league); continue; }

            await page.OpenAsync(At(href), log);
            // Matches of /bets/soccer/argentina/1970…-superliga live under /bets/soccer/argentina/superliga/.
            var countryPath = href[..href.LastIndexOf('/')];
            var matchSelector = $"a[href^='{countryPath}/{Slug(href)}/']";
            if (!await IsVisible(page.Locator(matchSelector).First, 20_000)) { log.LogInformation("Leonbet: no matches in {League}", league); continue; }
            var rows = await page.EvaluateAsync<string[][]>(
                "sel => [...document.querySelectorAll(sel)].map(a => [a.getAttribute('href'), a.innerText])", matchSelector);
            int before = listed.Count;
            var leagueName = (league.IndexOf('.') is var dot && dot > 0 ? league[(dot + 1)..] : league).Trim();
            foreach (var row in rows.DistinctBy(r => r[0]))
            {
                var (home, away, kickoff) = ParseMatchLink(row[1]);
                if (home is null || kickoff is null || kickoff > untilUtc) continue;
                // Outrights ("UEFA Nations League 2026/27" v "Winner") are listed like matches: skip them.
                if (home.Contains(leagueName, StringComparison.OrdinalIgnoreCase)) continue;
                listed.Add((At(row[0]), league, home, away!, kickoff.Value));
            }
            log.LogInformation("Leonbet: {Count} matches in {League} before {Until:yyyy-MM-dd HH:mm} UTC", listed.Count - before, league, untilUtc);
        }

        // 2) Each match page: the account's markets in order, Over/Under rows of the block titled e.g. "Total Corners".
        var result = new List<MatchInfo>();
        foreach (var m in listed)
        {
            ct.ThrowIfCancellationRequested();
            var sels = new List<Selection>();
            try
            {
                await page.OpenAsync(m.url, log);
                await page.WaitForTimeoutAsync(3_000);
                foreach (var choice in markets.Where(c => c.Leagues.Any(l => l.Trim().Equals(m.league.Trim(), StringComparison.OrdinalIgnoreCase))))
                {
                    var site = o.Markets.FirstOrDefault(x => x.Market.Equals(choice.Market, StringComparison.OrdinalIgnoreCase));
                    if (site is null) continue;
                    if (choice.IntervalKey is null)
                    {
                        if (string.IsNullOrEmpty(site.Title)) continue;
                        await OpenTabAsync(page, o.AllMarketsTab);
                        foreach (var (side, line, odds) in await ReadTotalAsync(page, site.Title))
                            sels.Add(new Selection(site.Market, side, line, odds, $"{m.url}|{site.Title}|{side}|{line.ToString(CultureInfo.InvariantCulture)}"));
                    }
                    else if (choice.IntervalFrom == 1 && site.IntervalTitles.Length > 0)
                    {
                        // Leonbet's interval markets run from kickoff to minute N, mostly on the "Intervals" tab
                        // (some, like "Goal Scored In The First 5 Minutes", may sit under "All markets": both are tried).
                        var to = choice.IntervalTo!.Value.ToString(CultureInfo.InvariantCulture);
                        foreach (var tab in new[] { o.IntervalsTab, o.AllMarketsTab })
                        {
                            await OpenTabAsync(page, tab);
                            if (await ReadIntervalAsync(page, m.url, site, choice, to, sels)) break;
                        }
                    }
                    if (sels.Any(x => x.Market == choice.Market && x.Side == choice.Side && x.Interval == choice.IntervalKey
                                      && (choice.RequiredLine is not { } want || x.Line == want)
                                      && x.Odds >= rules.MinPickOdds && x.Odds <= rules.MaxPickOdds))
                        break; // the slip builder takes the first usable market, so later ones need not be read
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                log.LogWarning("Leonbet: could not read markets for {Home} v {Away}: {Error}", m.home, m.away, ex.Message.Split('\n')[0]);
                await SaveDebug(page, "markets-failed", fullPage: false);
            }
            log.LogInformation("Leonbet: {Home} v {Away}: {Summary}", m.home, m.away,
                sels.Count == 0 ? "no total lines" : string.Join(", ", sels.Select(x => $"{x.Label ?? x.Market + " " + x.Side + " " + x.Line} @ {x.Odds:0.00}")));
            result.Add(new MatchInfo(m.url, m.league, m.home, m.away, m.kickoff, sels));
        }
        return result;
    }

    /// <summary>League link for "Country. League": its text is "Argentina - Super League 14" or "Argentina Super League".</summary>
    public static string? FindLeague(string[][] links, string league)
    {
        static string Clean(string t) => Regex.Replace(Regex.Replace(t, @"\s+\d+$", ""), @"\s+", " ").Trim(); // drop the match count
        var dot = league.IndexOf('.');
        var country = dot > 0 ? league[..dot].Trim() : null;
        var name = (dot > 0 ? league[(dot + 1)..] : league).Trim();
        if (country is not null)
            foreach (var l in links)
            {
                var text = Clean(l[1]);
                if (text.Equals($"{country} - {name}", StringComparison.OrdinalIgnoreCase) || text.Equals($"{country} {name}", StringComparison.OrdinalIgnoreCase))
                    return l[0];
            }
        // Spelling differences in spaces and punctuation only, e.g. "Spain. La Liga" for the site's "Spain - LaLiga".
        static string Squash(string t) => Regex.Replace(t, @"[^\p{L}\p{N}]", "").ToLowerInvariant();
        if (country is not null && links.Where(l => Squash(Clean(l[1])) == Squash(country + name)).Select(l => l[0]).Distinct().ToList() is [var only])
            return only;
        // No country given (e.g. "UEFA Nations League"): accept the league name alone if exactly one league has it.
        var byName = links.Where(l => Clean(l[1]).EndsWith(" - " + name, StringComparison.OrdinalIgnoreCase)).Select(l => l[0]).Distinct().ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    private static string Slug(string leagueHref) => Regex.Replace(leagueHref.Split('/')[^1], @"^\d+-", "");

    /// <summary>Match link text "Today\n22:45\nHome\nAway" / "Tomorrow …" / "09.10\n20:30\n…" → teams and kickoff (EAT → UTC).</summary>
    public static (string? home, string? away, DateTime? kickoffUtc) ParseMatchLink(string text, DateTime? nowEat = null)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        int t = lines.FindIndex(l => Regex.IsMatch(l, @"^\d{2}:\d{2}$"));
        if (t < 1 || t + 2 >= lines.Count) return (null, null, null);
        var today = (nowEat ?? Eat.Now(TimeProvider.System)).Date;
        var day = lines[t - 1];
        DateTime date;
        if (day.Equals("Today", StringComparison.OrdinalIgnoreCase)) date = today;
        else if (day.Equals("Tomorrow", StringComparison.OrdinalIgnoreCase)) date = today.AddDays(1);
        else if (DateTime.TryParseExact(day, "dd.MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dm))
        {
            date = new DateTime(today.Year, dm.Month, dm.Day);
            if (date < today.AddMonths(-6)) date = date.AddYears(1);
        }
        else return (null, null, null); // live or unknown
        var time = TimeSpan.ParseExact(lines[t], @"hh\:mm", CultureInfo.InvariantCulture);
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date + time, DateTimeKind.Unspecified), Eat.Zone);
        return (lines[t + 1], lines[t + 2], utc);
    }

    /// <summary>
    /// Reads the first of the market's interval blocks found on the open tab into <paramref name="sels"/>:
    /// "{to} Minute Result" → X (draw); "Total After {to} Minutes" → Over/Under rows; a Yes/No block such as
    /// "Goal Scored In The First {to} Minutes" → its "No" as Under 0.5 (nothing happens in the interval). False if none is there.
    /// </summary>
    private async Task<bool> ReadIntervalAsync(IPage page, string url, LeonbetMarket site, MarketChoice choice, string to, List<Selection> sels)
    {
        foreach (var template in site.IntervalTitles)
        {
            var title = template.Replace("{to}", to);
            if (site.Market == "result")
            {
                if (await ReadDrawAsync(page, title) is not { } drawOdds) continue;
                sels.Add(new Selection("result", "Draw", 0, drawOdds, $"{url}|{title}|X", choice.IntervalKey, $"{title}: X (draw)"));
                return true;
            }
            if (!await IsVisible(page.Locator($"text=/{PageText.TitlePattern(title)}/i >> visible=true").First, 5_000)) continue;
            // A Yes/No block first: the Over/Under reader climbs from the title and could reach a neighbouring block's rows.
            switch (await ReadNoAsync(page, title))
            {
                case (true, { } noOdds):
                    sels.Add(new Selection(site.Market, "Under", 0.5m, noOdds, $"{url}|{title}|No", choice.IntervalKey, $"{title}: No"));
                    return true;
                case (true, null):
                    continue; // a Yes/No block with "No" suspended: nothing to bet here
            }
            var rows = await ReadTotalAsync(page, title);
            foreach (var (side, line, odds) in rows)
                sels.Add(new Selection(site.Market, side, line, odds, $"{url}|{title}|{side}|{line.ToString(CultureInfo.InvariantCulture)}",
                    choice.IntervalKey, $"{title}: {side} ({line.ToString(CultureInfo.InvariantCulture)})"));
            if (rows.Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the visible block titled exactly <paramref name="title"/> is a Yes/No block, and the odds of its "No" button
    /// (null when it is suspended or unreadable).
    /// </summary>
    private async Task<(bool yesNo, decimal? noOdds)> ReadNoAsync(IPage page, string title)
    {
        var titleEl = page.Locator($"text=/{PageText.TitlePattern(title)}/i >> visible=true").First;
        if (!await IsVisible(titleEl, 1_000)) return (false, null);
        var runners = await titleEl.EvaluateAsync<string[][]>(RunnersScript, -1);
        var no = runners.FirstOrDefault(r => r[0].Trim().Equals("No", StringComparison.OrdinalIgnoreCase));
        if (no is null) return (false, null);
        if (no[2] == "true") { log.LogInformation("Leonbet: 'No' in '{Title}' is suspended", title); return (true, null); }
        return (true, decimal.TryParse(no[1].Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var odds) ? odds : null);
    }

    /// <summary>Over/Under rows ("Under (6.5)" + odds) of the visible block titled exactly <paramref name="title"/>.</summary>
    private async Task<List<(string side, decimal line, decimal odds)>> ReadTotalAsync(IPage page, string title)
    {
        var titleEl = page.Locator($"text=/{PageText.TitlePattern(title)}/i >> visible=true").First;
        if (!await IsVisible(titleEl, 8_000)) return [];
        var rows = await titleEl.EvaluateAsync<string[]>(RowsScript);
        var list = new List<(string, decimal, decimal)>();
        foreach (var r in rows)
            if (ParseRow(r) is { } p) list.Add(p);
        if (list.Count == 0) log.LogInformation("Leonbet: '{Title}' found but no rows parsed; raw: {Rows}", title, string.Join(" | ", rows.Take(6)));
        return list;
    }

    /// <summary>
    /// From a block title, climbs to the smallest ancestor holding "Over (x)"/"Under (x)" cells (the block) and returns each
    /// cell's text with its odds, e.g. "Under (6.5) 1.21".
    /// </summary>
    private const string RowsScript = @"title => {
        const isRow = e => e.children.length === 0 && /^\s*(Over|Under)\s*\(\s*[\d.,]+\s*\)\s*$/.test(e.textContent);
        const cellText = c => {
            let cell = c;
            for (let j = 0; j < 4 && cell.parentElement && !/\d\s*$/.test(cell.innerText.replace(/^\s*(Over|Under)\s*\(\s*[\d.,]+\s*\)/, '')); j++)
                cell = cell.parentElement;
            return cell.innerText.replace(/\s+/g, ' ').trim();
        };
        let n = title;
        for (let i = 0; i < 10 && n.parentElement; i++) {
            n = n.parentElement;
            const names = [...n.querySelectorAll('*')].filter(isRow);
            if (names.length) return names.map(cellText);
        }
        return [];
    }";

    /// <summary>Clicks a match-page tab ("All markets", "Intervals", …) if it is there.</summary>
    private async Task OpenTabAsync(IPage page, string tab)
    {
        var t = page.Locator($"text=/{PageText.TitlePattern(tab)}/i >> visible=true").First;
        if (!await IsVisible(t, 8_000)) return;
        // After reading a long tab the page is scrolled down and the tabs sit under the fixed top bar, where a normal click
        // times out (seen 2026-10-07: every match lost). Back to the top first; if still covered, send the click to the tab itself.
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        try { await t.ClickAsync(new() { Timeout = 5_000 }); }
        catch (TimeoutException) { await t.DispatchEventAsync("click"); }
        await page.WaitForTimeoutAsync(1_500);
    }

    /// <summary>Odds of the "X" (draw) outcome in the visible block titled exactly <paramref name="title"/>, e.g. "10 Minute Result".</summary>
    private async Task<decimal?> ReadDrawAsync(IPage page, string title)
    {
        var titleEl = page.Locator($"text=/{PageText.TitlePattern(title)}/i >> visible=true").First;
        if (!await IsVisible(titleEl, 3_000)) return null;
        var cell = await titleEl.EvaluateAsync<string?>(DrawScript);
        var m = cell is null ? null : Regex.Match(cell, @"^\s*X\s+(\d+(?:[.,]\d+)?)\s*$");
        return m is { Success: true } ? decimal.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : null;
    }

    /// <summary>From a block title, the cell text of its "X" outcome ("X 1.13"), or null.</summary>
    private const string DrawScript = @"title => {
        let n = title;
        for (let i = 0; i < 10 && n.parentElement; i++) {
            n = n.parentElement;
            const x = [...n.querySelectorAll('*')].find(e => e.children.length === 0 && e.textContent.trim() === 'X');
            if (x) {
                let cell = x;
                for (let j = 0; j < 4 && cell.parentElement && !/\d\s*$/.test(cell.innerText); j++) cell = cell.parentElement;
                return cell.innerText.replace(/\s+/g, ' ').trim();
            }
        }
        return null;
    }";

    /// <summary>"Under (6.5) 1.21" → ("Under", 6.5, 1.21).</summary>
    public static (string side, decimal line, decimal odds)? ParseRow(string text)
    {
        var m = Regex.Match(text, @"^\s*(Over|Under)\s*\(\s*(\d+(?:[.,]\d+)?)\s*\)\s+(\d+(?:[.,]\d+)?)\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var side = m.Groups[1].Value.Equals("under", StringComparison.OrdinalIgnoreCase) ? "Under" : "Over";
        return (side, decimal.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture),
                      decimal.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture));
    }

    private static async Task<bool> IsVisible(ILocator l, int timeoutMs)
    {
        try { await l.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs }); return true; }
        catch (TimeoutException) { return false; }
    }

    /// <summary>
    /// Fills the bet slip and types the stake (as <see cref="FillSlipAsync"/>), then clicks "Place bet".
    /// Anything wrong before the click throws: nothing is placed, the slip stays in the tab for the user.
    /// After the click, a missing confirmation throws <see cref="BetUnconfirmedException"/>.
    /// </summary>
    public async Task<string> PlaceSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
    {
        var balanceBefore = await GetBalanceAsync(ct);
        if (!await FillSlipAsync(picks, stake, ct))
            throw new InvalidOperationException($"The stake {stake:N0} could not be typed into Leonbet's bet slip, so Place was not clicked. The picks are in the slip in Chrome.");
        var page = await Page();
        var place = page.Locator(o.PlaceButton).Locator("visible=true").First;
        if (!await IsVisible(place, 10_000) || !await place.IsEnabledAsync())
            throw new InvalidOperationException("Leonbet's 'Place bet' button is not ready (odds changed or a message is shown). Nothing placed; check the slip in Chrome.");

        // From here the bet may exist: every failure is "unconfirmed", never "not placed".
        try
        {
            var id = await PlaceConfirm.ClickAndWaitAsync(page, place, "leonbet", () => GetBalanceAsync(ct), balanceBefore, stake, o.ConfirmationWaitSeconds, log);
            var shot = await SaveDebug(page, "after-place", fullPage: false);
            if (id is null)
                throw new BetUnconfirmedException(
                    $"'Place bet' was clicked ({picks.Count} picks, stake {stake:N0}) but neither a bet number nor a lower balance showed within " +
                    $"{o.ConfirmationWaitSeconds}s (screenshot: {shot}). Check 'My bets' on Leonbet.");
            browserTab.LeaveOpen = false; // placed: log out at the end like any run
            log.LogInformation("Leonbet: placed bet {Id} for {User}: {Count} picks, stake {Stake}", id, account.Username, picks.Count, stake);
            return id;
        }
        catch (Exception ex) when (ex is not BetUnconfirmedException)
        {
            var shot = await SaveDebug(page, "place-failed", fullPage: false);
            throw new BetUnconfirmedException($"Error after clicking 'Place bet': {ex.Message.Split('\n')[0]} (screenshot: {shot}). Check 'My bets' on Leonbet.", ex);
        }
    }

    /// <summary>
    /// Clicks each pick's odds button so Leonbet's bet slip holds them, checks the slip count, and leaves the tab open.
    /// The stake is typed; "Place" is not clicked here (PlaceSlipAsync does that when Betting:PlaceBets is on).
    /// </summary>
    public async Task<bool> FillSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
    {
        var page = await Page();
        await Step("fill bet slip", async () =>
        {
            var before = await SlipCountAsync(page);
            if (before != 0)
                throw new InvalidOperationException($"Leonbet's bet slip already holds {before} selections. Empty it on the site, then press Run now again.");
            foreach (var pick in picks)
            {
                ct.ThrowIfCancellationRequested();
                await AddPickAsync(page, pick);
            }
            var count = await SlipCountAsync(page);
            if (count != picks.Count)
                throw new InvalidOperationException($"Leonbet's bet slip shows {count} selections instead of {picks.Count}. Check it on the site before placing.");
        });
        await SaveDebug(page, "slip-filled", fullPage: false);
        await page.BringToFrontAsync();
        browserTab.LeaveOpen = true;
        // The system's stake goes into the stake box too; Place is left for the user.
        var stakeTyped = await StakeBox.TypeAsync(page, o.StakeInput, stake, log, "Leonbet");
        await SaveDebug(page, "stake-typed", fullPage: false);
        log.LogInformation("Leonbet: bet slip filled for {User} ({Count} picks, stake {Stake} {Typed}); left for the user to place", account.Username, picks.Count, stake, stakeTyped ? "typed" : "NOT typed");
        return stakeTyped;
    }

    /// <summary>Opens the pick's match, finds its odds button in the block named in the selection Ref, checks the odds and clicks it.</summary>
    private async Task AddPickAsync(IPage page, Pick pick)
    {
        // Ref = match url | block title | side | line, or match url | block title | X (draw) or No (Yes/No block)
        // (see GetMatchesAsync).
        var parts = pick.Selection.Ref.Split('|');
        var teams = $"{pick.Match.Home} v {pick.Match.Away}";
        var want = parts.Length == 3
            ? new Regex($@"^\s*{Regex.Escape(parts[2])}\s*$", RegexOptions.IgnoreCase)
            : new Regex($@"^\s*{Regex.Escape(parts[2])}\s*\(\s*{Regex.Escape(parts[3]).Replace(@"\.", @"[.,]")}\s*\)\s*$", RegexOptions.IgnoreCase);

        await page.OpenAsync(parts[0], log);
        await page.WaitForTimeoutAsync(3_000);
        await OpenTabAsync(page, pick.Selection.Interval is null ? o.AllMarketsTab : o.IntervalsTab);
        var titleEl = page.Locator($"text=/{PageText.TitlePattern(parts[1])}/i >> visible=true").First;
        if (!await IsVisible(titleEl, 8_000) && pick.Selection.Interval is not null)
            await OpenTabAsync(page, o.AllMarketsTab); // some interval blocks are listed under "All markets"
        if (!await IsVisible(titleEl, 5_000)) throw new InvalidOperationException($"'{parts[1]}' is no longer offered for {teams}.");

        var runners = await titleEl.EvaluateAsync<string[][]>(RunnersScript, -1);
        int i = Array.FindIndex(runners, r => want.IsMatch(r[0]));
        if (i < 0) throw new InvalidOperationException($"No '{SlipBuilder.SelectionText(pick.Selection)}' button in '{parts[1]}' for {teams}.");
        var (name, price, locked, selected) = (runners[i][0], runners[i][1], runners[i][2], runners[i][3]);
        if (locked == "true") throw new InvalidOperationException($"'{name}' in '{parts[1]}' is suspended for {teams}.");
        // Never at odds outside the rules, even if they moved since the slip was built.
        var odds = decimal.TryParse(price.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : (decimal?)null;
        if (odds is null || odds < rules.MinPickOdds || odds > rules.MaxPickOdds)
            throw new InvalidOperationException($"Odds moved for {teams}: '{name}' in '{parts[1]}' is now {price}.");
        if (selected == "true") throw new InvalidOperationException($"'{name}' for {teams} was already in the bet slip.");

        await titleEl.EvaluateAsync<string[][]>(RunnersScript, i); // marks the button so it can be clicked like a person would
        var button = page.Locator("[data-mikeka-pick='1']").First;
        await button.ScrollIntoViewIfNeededAsync(new() { Timeout = 10_000 });
        await button.ClickAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(1_500);
        if (await button.GetAttributeAsync("data-test-attr-selected") != "true")
            throw new InvalidOperationException($"Clicked '{name}' @ {price} for {teams} but it did not go into the bet slip.");
        await button.EvaluateAsync("b => b.removeAttribute('data-mikeka-pick')");
        log.LogInformation("Leonbet: bet slip has {Teams}: {Title} {Name} @ {Odds}", teams, parts[1], name, price);
    }

    /// <summary>
    /// From a block title, the odds buttons of that block as [name, price, locked, selected]. With mark ≥ 0 that button
    /// also gets data-mikeka-pick="1" (any earlier mark is removed).
    /// </summary>
    private const string RunnersScript = @"(title, mark) => {
        document.querySelectorAll('[data-mikeka-pick]').forEach(e => e.removeAttribute('data-mikeka-pick'));
        let n = title;
        for (let i = 0; i < 10 && n.parentElement; i++) {
            n = n.parentElement;
            const buttons = [...n.querySelectorAll(""[data-test-el='sportline-runner']"")];
            if (!buttons.length) continue;
            if (mark >= 0 && buttons[mark]) buttons[mark].setAttribute('data-mikeka-pick', '1');
            const text = (b, el) => (b.querySelector(`[data-test-el='${el}']`)?.textContent ?? '').replace(/\s+/g, ' ').trim();
            return buttons.map(b => [text(b, 'sportline-runner-name'), text(b, 'sportline-runner-price'),
                b.getAttribute('data-test-attr-locked') ?? '', b.getAttribute('data-test-attr-selected') ?? '']);
        }
        return [];
    }";

    /// <summary>The number on the "Bet slip" tab of the right-hand panel.</summary>
    private static async Task<int> SlipCountAsync(IPage page)
    {
        var tab = page.Locator("[data-test-id='tab-slip']").First;
        if (!await IsVisible(tab, 10_000)) throw new InvalidOperationException("Leonbet's bet slip panel is not shown.");
        var m = Regex.Match(await tab.InnerTextAsync(), @"(\d+)\s*$");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    /// <summary>
    /// Leonbet gives no bet number, so the bet is found on the bet-history page by its teams (<see cref="BetHistory"/>).
    /// Not found = Pending, with a "history-not-found" screenshot to correct <c>HistoryPaths</c>.
    /// </summary>
    public async Task<BetOutcome> GetOutcomeAsync(Slip slip, CancellationToken ct)
    {
        var page = await Page();
        var found = await BetHistory.ReadAsync(page, At, o.HistoryPaths, o.HistoryLinkRegex, slip, log);
        if (found is null)
        {
            var shot = await SaveDebug(page, "history-not-found");
            log.LogWarning("Leonbet: slip #{Id} not found in the bet history (screenshot {Shot})", slip.Id, shot);
            return BetOutcome.Pending;
        }
        log.LogInformation("Leonbet: slip #{Id} in history → {Outcome}: {Text}", slip.Id, found.Value.outcome, found.Value.text);
        return found.Value.outcome;
    }

    /// <summary>
    /// Logs out at the end of a run: profile avatar → Settings (/profile/settings) → "Log out" → "Log out?" confirm → checks
    /// the header "Log in" is back (seen on the live site 2026-10-06). If not, the account's Activity says so.
    /// </summary>
    private async Task LogoutAsync()
    {
        if (_page is null || _page.IsClosed || !_loggedIn) return;
        var page = _page;
        try
        {
            // Settings holds the Log out button; its address opens it directly (the avatar click is the fallback).
            await page.OpenAsync(At(o.SettingsPath), log);
            var logout = page.Locator(o.LogoutButton).First;
            if (!await IsVisible(logout, 10_000))
            {
                await page.Locator(o.ProfileButton).First.ClickAsync(new() { Timeout = 5_000 });
                await page.WaitForTimeoutAsync(1_500);
                await page.Locator($"a[href='{o.SettingsPath}']").First.ClickAsync(new() { Timeout = 5_000 });
            }
            await logout.ClickAsync(new() { Timeout = 10_000 });
            await page.Locator(o.LogoutConfirm).First.ClickAsync(new() { Timeout = 10_000 }); // "Log out?" → Log out
            if (await IsVisible(page.Locator(o.HeaderLogin).First, 15_000))
            {
                _loggedIn = false;
                log.LogInformation("Logged out of {Site} ({User})", _origin.Host, account.Username);
                return;
            }
            var shot = await SaveDebug(page, "logout-not-done", fullPage: false);
            await browserTab.NoteAsync($"Leonbet: Log out was clicked but {account.Username} still looks logged in (screenshot: {shot}). Please log out by hand in the Leonbet tab.");
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException)
        {
            var shot = await SaveDebug(page, "logout-failed", fullPage: false);
            await browserTab.NoteAsync($"Leonbet: log out failed ({ex.Message.Split('\n')[0]}; screenshot: {shot}). Please log out by hand in the Leonbet tab.");
        }
    }

    private async Task Step(string name, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            var file = _page is null ? null : await SaveDebug(_page, name.Replace(' ', '-'));
            throw new InvalidOperationException($"Leonbet step '{name}' failed: {ex.Message.Split('\n')[0]}" + (file is null ? "" : $" (screenshot: {file})"), ex);
        }
    }

    private async Task<string?> SaveDebug(IPage page, string name, bool fullPage = true)
    {
        try
        {
            Directory.CreateDirectory(o.DebugDir);
            var stem = Path.Combine(o.DebugDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{name}");
            await page.ScreenshotAsync(new() { Path = stem + ".png", FullPage = fullPage });
            await File.WriteAllTextAsync(stem + ".html", await page.ContentAsync());
            return stem + ".png";
        }
        catch { return null; }
    }

    public async ValueTask DisposeAsync()
    {
        // Log out at the end of a run, unless the bet slip was filled for the user to place.
        if (!browserTab.LeaveOpen) await LogoutAsync();
        await browserTab.DisposeAsync(); // the site's tab stays open for the next run (with the filled bet slip, if any)
    }
}
