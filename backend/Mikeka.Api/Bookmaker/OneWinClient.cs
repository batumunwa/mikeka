using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Api.Bookmaker;

/// <summary>Where a market lives on a 1win match page: the tab to open and the exact block title.</summary>
public class OneWinMarket
{
    public string Market { get; set; } = "";
    /// <summary>Match page tab, e.g. "Corners". Empty = the tab shown when the page opens.</summary>
    public string Tab { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Tab and title of interval blocks, e.g. "Intervals" + "Total from {from} to {to} minute". Empty = none.</summary>
    public string IntervalTab { get; set; } = "";
    public string IntervalTitle { get; set; } = "";
}

/// <summary>
/// Page settings for 1win ("OneWin" section in appsettings.json). Leagues are named "Country. League" as 1win shows them,
/// e.g. "Argentina. Liga Profesional". Market block titles were read from the live site on 2026-10-05.
/// </summary>
public class OneWinOptions
{
    public string DebugDir { get; set; } = "logs/1win-debug";
    /// <summary>Bet-history pages tried in turn to read a slip's result (not seen on the live site yet: check the first run).</summary>
    public string[] HistoryPaths { get; set; } = ["/betting/bets-history"]; // the Sports page's "Bet history" tab (seen 2026-10-08)
    /// <summary>Menu link to the bet history, clicked when no path shows the bet.</summary>
    public string HistoryLinkRegex { get; set; } = @"^\s*(my bets|bet history|bets history|betting history|history)\s*$";
    public string FootballPath { get; set; } = "/betting/prematch/football-18";
    public string MatchCard { get; set; } = "[data-qa='match-card']";
    /// <summary>League links shown under an opened country row (seen on the live site on 2026-10-06).</summary>
    public string LeagueLink { get; set; } = "a[href^='/betting/prematch/football-18/']";
    /// <summary>
    /// One selection in the bet slip ("Betslip" panel on the right). Singles get a CouponCard each; a Multiple is one
    /// CouponCard holding all selections, so selections are counted, not cards.
    /// </summary>
    public string BetSlipItem { get; set; } = "[data-scope='Betslip'] [data-scope='SelectionInfo']";
    /// <summary>Shown under a Multiple (accumulator) only: "Final odds 2.27".</summary>
    public string MultipleOddsText { get; set; } = "Final odds";
    /// <summary>Where 1win keeps the bet slip between page loads (page storage).</summary>
    public string BetSlipStorageKey { get; set; } = "_bet_frame_last_coupons";
    /// <summary>Bet slip tab for an accumulator.</summary>
    public string MultipleTab { get; set; } = "Multiple";
    public int MaxMatchesPerLeague { get; set; } = 40;

    public OneWinMarket[] Markets { get; set; } =
    [
        new() { Market = "goals", Tab = "", Title = "Total", IntervalTab = "Intervals", IntervalTitle = "Total from {from} to {to} minute" },
        new() { Market = "corners", Tab = "Corners", Title = "Corners. Total" },
        new() { Market = "cards", Tab = "Cards/Penalties", Title = "Yellow cards. Total" },
        new() { Market = "fouls", Tab = "Fouls", Title = "Fouls. Total" }, // not seen on the site yet
    ];

    // Login: data-testid labels seen on the live site (2026-10-05). The form opens on the Phone tab.
    public string LoginButton { get; set; } = "[data-testid='header-auth-button']";
    /// <summary>When the automatic login hangs, how long to wait for the user to log in by hand in the tab.</summary>
    public int ManualLoginWaitMinutes { get; set; } = 10;
    /// <summary>When the puzzle or a login by hand is still not done after this long, the user gets an email.</summary>
    public int EmailAfterSeconds { get; set; } = 60;
    /// <summary>
    /// GeeTest "verify you are human" box (1win sets a gcaptcha4.geetest.com cookie, seen 2026-10-07). It seems to appear on
    /// the first login after Chrome starts. The system never solves it: it asks the user to, and then carries on.
    /// </summary>
    public string CaptchaBox { get; set; } = "[class*='geetest_box'], [class*='geetest_captcha'], [class*='geetest_holder'], [class*='geetest_panel']";
    /// <summary>Close buttons of pop-ups 1win shows over the page (e.g. "Stay updated" notifications after login).</summary>
    public string PopupClose { get; set; } = "[data-testid='subscribeNotificationPopup-close']";
    // Log out: sidebar profile → Settings → Log out → "Log out" again in the confirmation (seen 2026-10-06).
    public string ProfileButton { get; set; } = "[data-testid='sidebar-profile-button']";
    public string SettingsButton { get; set; } = "[data-testid='profile-settingsButton']";
    public string LogoutButton { get; set; } = "[data-testid='userSettings-logoutButton']";
    /// <summary>The confirmation's Log out button: a button in the pop-up window, other than the Settings one.</summary>
    public string LogoutConfirm { get; set; } = "[data-testid='desktop-modal'] button:not([data-testid='userSettings-logoutButton'])";
    public string LogoutRegex { get; set; } = @"^\s*log ?out\s*$";
    /// <summary>The bet slip's stake box ("Bet amount").</summary>
    public string StakeInput { get; set; } = "input[data-qa='amount']";
    /// <summary>The bet slip's place button: a button in the Betslip panel whose text matches PlaceButtonText.</summary>
    public string PlaceButton { get; set; } = "[data-scope='Betslip'] button";
    public string PlaceButtonText { get; set; } = @"^\s*Place a bet\s*$";
    /// <summary>How long to wait after "Place a bet" for the bet to be confirmed.</summary>
    public int ConfirmationWaitSeconds { get; set; } = 30;
    public string EmailTab { get; set; } = "[data-testid$='form-tab-email']";
    public string UsernameInput { get; set; } = "input[data-testid*='form-email'], input[data-testid*='form-login'], input[type='email'] >> visible=true";
    public string PhoneInput { get; set; } = "input[data-testid$='form-phone'] >> visible=true";
    public string PasswordInput { get; set; } = "input[data-testid$='form-password'] >> visible=true";
    public string LoginSubmit { get; set; } = "button[data-testid$='form-submit'] >> visible=true";
    public string Balance { get; set; } = "[data-qa*='balance' i], [class*='balance' i]";
}

public class OneWinFactory(IOptions<OneWinOptions> options, SharedBrowser browser, IOptions<BettingRules> rules, INotifier notifier, ILogger<OneWinClient> log)
{
    public async Task<IBookmakerClient> CreateAsync(Account account, string password, CancellationToken ct)
    {
        var o = options.Value;
        var tab = await browser.OpenTabAsync(account, o.LoginButton, ct);
        return new OneWinClient(tab, account, password, o, rules.Value.ForAccount(account), notifier, log);
    }
}

public sealed class OneWinClient(
    BrowserTab browserTab, Account account, string password,
    OneWinOptions o, BettingRules rules, INotifier notifier, ILogger log) : IBookmakerClient
{
    private readonly Uri _origin = new(new Uri(account.Url).GetLeftPart(UriPartial.Authority));
    private IPage? _page;
    private Task<IPage> Page() => Task.FromResult(_page ??= browserTab.Page);
    private string At(string path) => new Uri(_origin, path).ToString();

    // ---------- Login / balance (to be confirmed with a real account) ----------

    public async Task LoginAsync(CancellationToken ct)
    {
        var page = await Page();
        // Start clean: an earlier account's leftovers make 1win refuse the next login until the browser data is cleared.
        await ClearSiteDataAsync(page);
        await ClearHistoryAsync(page);
        await Step("open site", () => page.OpenAsync(At("/betting"), log));
        await Step("login", async () =>
        {
            // After the clearing above no earlier session should be left; if one is, it is a login made by hand just now.
            var loginButton = page.Locator(o.LoginButton).First;
            if (!await IsVisible(loginButton, 30_000))
            {
                if (await IsVisible(page.Locator(o.Balance).First, 10_000))
                {
                    log.LogWarning("1win: already logged in from an earlier run; continuing with that session as {User}", account.Username);
                    return;
                }
                throw new TimeoutException("Neither the Login button nor a balance appeared on 1win.");
            }
            await loginButton.ClickAsync(new() { Timeout = 30_000 });
            if (account.Username.Contains('@'))
            {
                await page.Locator(o.EmailTab).First.ClickAsync(new() { Timeout = 10_000 }); // form opens on the Phone tab
                await page.Locator(o.UsernameInput).First.FillAsync(account.Username, new() { Timeout = 15_000 });
            }
            else
                await page.Locator(o.PhoneInput).First.FillAsync(Regex.Replace(account.Username, @"^\+?255", ""), new() { Timeout = 15_000 });
            await page.Locator(o.PasswordInput).First.FillAsync(password, new() { Timeout = 15_000 });
            await page.Locator(o.LoginSubmit).First.ClickAsync(new() { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(3_000);
            await SaveDebug(page, "login-clicked", fullPage: false); // what 1win shows right after the click (puzzle, spinner, error)
            // The button spins while 1win logs in; done when the header Login button has gone. A GeeTest check is handed
            // to the user at once (it is never solved by the system); the form stays filled, so solving it finishes the login.
            try
            {
                var captcha = page.Locator(o.CaptchaBox).Locator("visible=true").First;
                var done = false;
                for (int waited = 0; waited < 90 && !done; waited += 2)
                {
                    if (!await page.Locator(o.LoginButton).First.IsVisibleAsync()) { done = true; break; }
                    if (await captcha.CountAsync() > 0)
                    {
                        var shot = await SaveDebug(page, "captcha", fullPage: false);
                        log.LogWarning("1win: GeeTest check shown at login for {User} (screenshot {Shot})", account.Username, shot);
                        await browserTab.NoteAsync(
                            $"1win asks to verify you are human (GeeTest puzzle) for {account.Username}. Please complete it in the 1win tab in Chrome " +
                            $"(the login form is already filled); the run continues by itself (waiting up to {o.ManualLoginWaitMinutes} minutes).");
                        await page.BringToFrontAsync();
                        await WaitForUserAsync(page, $"1win puzzle waiting for {account.Username}",
                            $"1win shows the \"verify you are human\" puzzle at the login of {account.Username}. Please solve it in the 1win tab " +
                            "of the Mikeka Chrome (the login form is already filled); the run then continues by itself.");
                        await browserTab.NoteAsync("1win: check completed; the run continues.");
                        done = true;
                        break;
                    }
                    await page.WaitForTimeoutAsync(2_000);
                }
                if (!done) throw new TimeoutException("1win login still spinning after 90 s.");
            }
            catch (TimeoutException)
            {
                // 1win sometimes never finishes a login typed by the system (the button keeps spinning). A login made by hand
                // in the same tab is kept by 1win, so ask the user to do it and carry on once it is done.
                await SaveDebug(page, "after-login", fullPage: false);
                await browserTab.NoteAsync(
                    $"1win did not finish the automatic login. Please log in by hand in the 1win tab in Chrome as {account.Username}; " +
                    $"the run continues by itself (waiting up to {o.ManualLoginWaitMinutes} minutes).");
                await page.BringToFrontAsync();
                try
                {
                    await WaitForUserAsync(page, $"1win login by hand needed for {account.Username}",
                        $"1win did not finish the automatic login of {account.Username} (no puzzle shown). Please log in by hand as " +
                        $"{account.Username} in the 1win tab of the Mikeka Chrome; the run then continues by itself.");
                    await page.WaitForTimeoutAsync(3_000);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException($"Still logged out of 1win after waiting {o.ManualLoginWaitMinutes} minutes for a login by hand (see after-login screenshot).");
                }
                await browserTab.NoteAsync("1win: logged in by hand; the run continues.");
            }
            await page.WaitForTimeoutAsync(3_000);
            await SaveDebug(page, "logged-in", fullPage: false); // to find the balance on the logged-in page
        });
        log.LogInformation("Logged in to {Site} as {User}", _origin.Host, account.Username);
    }

    /// <summary>
    /// Waits up to ManualLoginWaitMinutes for the user to finish the login in the tab (the header Login button goes). If it is
    /// not done within EmailAfterSeconds, the user gets an email saying until when the run waits.
    /// </summary>
    private async Task WaitForUserAsync(IPage page, string subject, string body)
    {
        var loginButton = page.Locator(o.LoginButton).First;
        var total = o.ManualLoginWaitMinutes * 60_000;
        var first = Math.Min(o.EmailAfterSeconds * 1_000, total);
        var until = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddMilliseconds(total), Eat.Zone);
        try
        {
            await loginButton.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = first });
            return;
        }
        catch (TimeoutException) when (first < total) { }
        try { await notifier.SendAsync(subject, $"{body}\n\nThe run waits until {until:HH:mm} EAT; after that it tries again at a later check.", CancellationToken.None); }
        catch (Exception ex) { log.LogWarning(ex, "1win: could not send the email '{Subject}'", subject); }
        await loginButton.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = total - first });
    }

    public async Task<decimal> GetBalanceAsync(CancellationToken ct)
    {
        var page = await Page();
        string text = "";
        await Step("read balance", async () => text = await page.Locator(o.Balance).First.InnerTextAsync(new() { Timeout = 15_000 }));
        var m = Regex.Match(text.Replace(" ", "").Replace(" ", "").Replace(",", ""), @"\d+(\.\d+)?");
        return m.Success ? decimal.Parse(m.Value, CultureInfo.InvariantCulture) : throw new InvalidOperationException($"Could not read a balance from '{text}'");
    }

    // ---------- Matches and markets ----------

    public async Task<IReadOnlyList<MatchInfo>> GetMatchesAsync(IReadOnlyCollection<string> leagues, IReadOnlyList<MarketChoice> markets, DateTime untilUtc, CancellationToken ct)
    {
        var page = await Page();
        var listed = new List<(string url, string league, string home, string away, DateTime kickoff)>();

        foreach (var league in leagues)
        {
            ct.ThrowIfCancellationRequested();
            if (!await OpenLeagueAsync(page, league)) continue;
            var leagueUrl = page.Url;
            var cards = await page.Locator(o.MatchCard).CountAsync();
            int before = listed.Count;
            for (int i = 0; i < Math.Min(cards, o.MaxMatchesPerLeague); i++)
            {
                var card = page.Locator(o.MatchCard).Nth(i);
                var (home, away, kickoff) = ParseCard(await card.InnerTextAsync());
                if (home is null || kickoff is null || kickoff > untilUtc) continue;
                if (kickoff <= DateTime.UtcNow) continue; // already started: nothing to bet before kick-off
                // Cards have no links: open the match to learn its address, then come back to the league.
                // The click goes on the home team's name, never the middle of the card where the odds buttons are.
                try
                {
                    await DismissPopupsAsync(page);
                    await card.GetByText(home, new() { Exact = true }).First.ClickAsync(new() { Timeout = 10_000 });
                    await page.WaitForURLAsync(new Regex("/betting/match/"), new() { Timeout = 20_000 });
                    listed.Add((page.Url, league, home, away!, kickoff.Value));
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    log.LogWarning("1win: could not open {Home} v {Away} in {League}, skipped: {Error}", home, away, league, ex.Message.Split('\n')[0]);
                    await SaveDebug(page, "match-open-failed", fullPage: false);
                }
                if (page.Url != leagueUrl) await page.OpenAsync(leagueUrl, log);
                await page.Locator(o.MatchCard).First.WaitForAsync(new() { Timeout = 30_000 });
            }
            log.LogInformation("1win: {Count} matches in {League} before {Until:yyyy-MM-dd HH:mm} UTC", listed.Count - before, league, untilUtc);
        }

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
                        foreach (var (side, line, odds) in await ReadTotalAsync(page, site.Tab, site.Title))
                            sels.Add(new Selection(site.Market, side, line, odds, $"{m.url}|{site.Tab}|{site.Title}|{side}|{line.ToString(CultureInfo.InvariantCulture)}"));
                    }
                    else if (!string.IsNullOrEmpty(site.IntervalTitle))
                    {
                        var title = site.IntervalTitle.Replace("{from}", choice.IntervalFrom.ToString()).Replace("{to}", choice.IntervalTo.ToString());
                        // All lines of that interval's total; the slip builder keeps the user's value (or 0.5 = "none").
                        foreach (var (side, line, odds) in await ReadTotalAsync(page, site.IntervalTab, title))
                            sels.Add(new Selection(site.Market, side, line, odds, $"{m.url}|{site.IntervalTab}|{title}|{side}|{line.ToString(CultureInfo.InvariantCulture)}", choice.IntervalKey));
                    }
                    if (sels.Any(x => x.Market == choice.Market && x.Side == choice.Side && x.Interval == choice.IntervalKey
                                      && (choice.RequiredLine is not { } want || x.Line == want)
                                      && x.Odds >= rules.MinPickOdds && x.Odds <= rules.MaxPickOdds))
                        break; // the slip builder takes the first usable choice
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                log.LogWarning("1win: could not read markets for {Home} v {Away}: {Error}", m.home, m.away, ex.Message.Split('\n')[0]);
                await SaveDebug(page, "markets-failed", fullPage: false);
            }
            log.LogInformation("1win: {Home} v {Away}: {Summary}", m.home, m.away,
                sels.Count == 0 ? "no total lines" : string.Join(", ", sels.GroupBy(x => x.Market + (x.Interval is null ? "" : " " + x.Interval)).Select(g => $"{g.Count()} {g.Key}")));
            result.Add(new MatchInfo(m.url, m.league, m.home, m.away, m.kickoff, sels));
        }
        return result;
    }

    /// <summary>Football → country → league by clicking, for a league named "Country. League". False if not offered.</summary>
    private async Task<bool> OpenLeagueAsync(IPage page, string league)
    {
        // "Argentina. Liga Profesional" or "Argentina.Liga Profesional": country before the first dot, league after it.
        var dot = league.IndexOf('.');
        if (dot <= 0 || dot == league.Length - 1) { log.LogWarning("1win: league '{League}' should be 'Country. League'", league); return false; }
        var (country, name) = (league[..dot].Trim(), league[(dot + 1)..].Trim());
        await page.OpenAsync(At(o.FootballPath), log);
        await page.Locator(o.MatchCard).First.WaitForAsync(new() { Timeout = 30_000 });
        // The country row, not a team of the same name in a match card (e.g. "Argentina" v Benin in the friendlies list).
        var found = await page.EvaluateAsync<bool>(@"([name, card]) => {
            document.querySelectorAll('[data-mikeka-country]').forEach(e => e.removeAttribute('data-mikeka-country'));
            const row = [...document.querySelectorAll('div, span')].find(e => e.children.length === 0 && !e.closest(card)
                && e.textContent.trim().toLowerCase() === name.toLowerCase());
            if (row) row.setAttribute('data-mikeka-country', '1');
            return !!row;
        }", new[] { country, o.MatchCard });
        if (!found) { log.LogWarning("1win: country '{Country}' not listed", country); return false; }
        var countryRow = page.Locator("[data-mikeka-country='1']").First;
        await countryRow.ScrollIntoViewIfNeededAsync(new() { Timeout = 10_000 });
        await countryRow.ClickAsync();
        await page.WaitForTimeoutAsync(2_000);
        // The country's leagues are links "/betting/prematch/football-18/liga-profesional-21807" with text "Liga Profesional 31".
        var links = await page.EvaluateAsync<string[][]>(
            "sel => [...document.querySelectorAll(sel)].map(a => [a.getAttribute('href'), a.innerText.replace(/\\s+/g, ' ').trim()])", o.LeagueLink);
        var href = links.FirstOrDefault(l => Regex.Replace(l[1], @"\s+\d+$", "").Equals(name, StringComparison.OrdinalIgnoreCase))?[0];
        if (href is null) { log.LogWarning("1win: league '{League}' not listed under {Country}", name, country); return false; }
        await page.OpenAsync(At(href), log);
        if (!await IsVisible(page.Locator(o.MatchCard).First, 20_000)) { log.LogInformation("1win: no matches in {League}", league); return false; }
        return true;
    }

    /// <summary>Card text "22:45 • 05/10/2026 Home Away Full time result …" → teams and kickoff (EAT → UTC).</summary>
    /// <summary>Closes 1win pop-ups that sit over the page (they would catch the system's clicks).</summary>
    private async Task DismissPopupsAsync(IPage page)
    {
        var close = page.Locator(o.PopupClose).First;
        try
        {
            if (await close.IsVisibleAsync())
            {
                await close.ClickAsync(new() { Timeout = 5_000 });
                await page.WaitForTimeoutAsync(500);
                log.LogInformation("1win: closed a pop-up over the page");
            }
        }
        catch (PlaywrightException) { }
        catch (TimeoutException) { }
    }

    public static (string? home, string? away, DateTime? kickoffUtc) ParseCard(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l != "•").ToList();
        int t = lines.FindIndex(l => Regex.IsMatch(l, @"^\d{2}:\d{2}$"));
        int d = lines.FindIndex(l => Regex.IsMatch(l, @"^\d{2}/\d{2}/\d{4}$"));
        if (t < 0 || d < 0 || d + 2 >= lines.Count) return (null, null, null);
        if (!DateTime.TryParseExact($"{lines[d]} {lines[t]}", "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return (null, null, null);
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Eat.Zone);
        return (lines[d + 1], lines[d + 2], utc);
    }

    /// <summary>Opens a tab (if any) and reads the Over/Under rows of the block titled exactly <paramref name="title"/>.</summary>
    private async Task<List<(string side, decimal line, decimal odds)>> ReadTotalAsync(IPage page, string tab, string title)
    {
        var titleEl = await FindBlockAsync(page, tab, title);
        if (titleEl is null) return [];
        await page.EvaluateAsync(PageText.CellTextScript);
        var rows = await titleEl.EvaluateAsync<string[]>(PageText.TotalRowsScript);
        var list = rows.Select(PageText.ParseTotalRow).Where(r => r.HasValue).Select(r => r!.Value).ToList();
        if (list.Count == 0) log.LogInformation("1win: '{Title}' found but no rows parsed; raw: {Rows}", title, string.Join(" | ", rows.Take(6)));
        return list;
    }

    private async Task<ILocator?> FindBlockAsync(IPage page, string tab, string title)
    {
        if (!string.IsNullOrEmpty(tab))
        {
            // Tabs load a little after the page; wait for them before deciding the match has no such tab.
            var t = page.Locator($"text=/{PageText.TitlePattern(tab)}/i >> visible=true").First;
            if (!await IsVisible(t, 15_000))
            {
                log.LogInformation("1win: tab '{Tab}' not found on {Url}", tab, page.Url);
                await SaveDebug(page, "no-tab-" + tab.Replace('/', '-').ToLowerInvariant(), fullPage: false);
                return null;
            }
            await t.ClickAsync();
            await page.WaitForTimeoutAsync(2_000);
        }
        var titleEl = page.Locator($"text=/{PageText.TitlePattern(title)}/i >> visible=true").First;
        if (await IsVisible(titleEl, 10_000)) return titleEl;
        log.LogInformation("1win: '{Title}' not found on {Url}", title, page.Url);
        await SaveDebug(page, "no-block", fullPage: false);
        return null;
    }

    // ---------- Bet slip and history (to be built with a real account) ----------

    /// <summary>
    /// Fills the bet slip and types the stake (as <see cref="FillSlipAsync"/>), then clicks "Place a bet".
    /// Anything wrong before the click throws: nothing is placed, the slip stays in the tab for the user.
    /// After the click, a missing confirmation throws <see cref="BetUnconfirmedException"/>.
    /// </summary>
    public async Task<string> PlaceSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
    {
        var balanceBefore = await GetBalanceAsync(ct);
        if (!await FillSlipAsync(picks, stake, ct))
            throw new InvalidOperationException($"The stake {stake:N0} could not be typed into 1win's bet slip, so Place was not clicked. The picks are in the slip in Chrome.");
        var page = await Page();
        await DismissPopupsAsync(page);
        var place = page.Locator(o.PlaceButton).Filter(new() { HasTextRegex = new Regex(o.PlaceButtonText, RegexOptions.IgnoreCase) }).Locator("visible=true").First;
        if (!await IsVisible(place, 10_000) || !await place.IsEnabledAsync())
            throw new InvalidOperationException("1win's 'Place a bet' button is not ready (odds changed or a message is shown). Nothing placed; check the slip in Chrome.");

        // From here the bet may exist: every failure is "unconfirmed", never "not placed".
        try
        {
            var id = await PlaceConfirm.ClickAndWaitAsync(page, place, "1win", () => GetBalanceAsync(ct), balanceBefore, stake, o.ConfirmationWaitSeconds, log);
            var shot = await SaveDebug(page, "after-place", fullPage: false);
            if (id is null)
                throw new BetUnconfirmedException(
                    $"'Place a bet' was clicked ({picks.Count} picks, stake {stake:N0}) but neither a bet number nor a lower balance showed within " +
                    $"{o.ConfirmationWaitSeconds}s (screenshot: {shot}). Check 'Bet history' on 1win.");
            browserTab.LeaveOpen = false; // placed: nothing left in the slip for the user
            // The card in Bet history carries 1win's own bet ID: kept as the bet number, so the result is read from exactly that card.
            var combined = picks.Aggregate(1m, (acc, p) => acc * p.Odds);
            try
            {
                if (await FindCardAsync(page, DateTime.UtcNow, picks.Count, combined, 0.05m) is { } card)
                {
                    log.LogInformation("1win: placed bet has ID {BetId} in Bet history: {Text}", card.Id, card.Text);
                    id = card.Id;
                }
                else log.LogWarning("1win: placed bet not found in Bet history yet; kept {Ref} (matched by time and odds later)", id);
            }
            catch (Exception ex) // the bet is placed: a failed lookup must never make it "unconfirmed"
            {
                log.LogWarning("1win: Bet history not read after placing ({Error}); kept {Ref}", ex.Message.Split('\n')[0], id);
            }
            log.LogInformation("1win: placed bet {Id} for {User}: {Count} picks, stake {Stake}", id, account.Username, picks.Count, stake);
            return id;
        }
        catch (Exception ex) when (ex is not BetUnconfirmedException)
        {
            var shot = await SaveDebug(page, "place-failed", fullPage: false);
            throw new BetUnconfirmedException($"Error after clicking 'Place a bet': {ex.Message.Split('\n')[0]} (screenshot: {shot}). Check 'Bet history' on 1win.", ex);
        }
    }

    /// <summary>
    /// Clicks each pick's odds button so 1win's bet slip holds them, picks "Multiple" for 2+ picks, checks the slip and
    /// leaves the tab open. The stake is typed; "Place a bet" is not clicked here (PlaceSlipAsync does that when Betting:PlaceBets is on).
    /// </summary>
    public async Task<bool> FillSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
    {
        var page = await Page();
        await Step("fill bet slip", async () =>
        {
            // Start from an empty bet slip (one left by an earlier, failed run is emptied).
            if (await page.Locator(o.BetSlipItem).CountAsync() > 0) await ClearBetSlipAsync(page);
            var before = await page.Locator(o.BetSlipItem).CountAsync();
            if (before != 0)
                throw new InvalidOperationException($"1win's bet slip already holds {before} selections. Empty it on the site, then press Run now again.");
            try
            {
                foreach (var pick in picks)
                {
                    ct.ThrowIfCancellationRequested();
                    await AddPickAsync(page, pick);
                }
                // Picks from different matches make a Multiple (1win switches by itself; the tab is clicked to be sure).
                if (picks.Count > 1)
                {
                    var multiple = page.Locator("button").Filter(new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(o.MultipleTab)}\\s*$", RegexOptions.IgnoreCase) }).Locator("visible=true").First;
                    if (await IsVisible(multiple, 5_000)) { await multiple.ClickAsync(); await page.WaitForTimeoutAsync(1_000); }
                    if (!await IsVisible(page.GetByText(o.MultipleOddsText).First, 5_000))
                        throw new InvalidOperationException("1win's bet slip is not a Multiple (no 'Final odds' shown).");
                }
                var count = await page.Locator(o.BetSlipItem).CountAsync();
                if (count != picks.Count)
                    throw new InvalidOperationException($"1win's bet slip shows {count} selections instead of {picks.Count}.");
            }
            catch
            {
                try { await ClearBetSlipAsync(page); } catch (Exception ex) { log.LogWarning("Could not empty the 1win bet slip: {Error}", ex.Message); }
                throw;
            }
        });
        await SaveDebug(page, "slip-filled", fullPage: false);
        await page.BringToFrontAsync();
        browserTab.LeaveOpen = true;
        // The system's stake goes into the stake box too; Place is left for the user.
        var stakeTyped = await StakeBox.TypeAsync(page, o.StakeInput, stake, log, "1win");
        await SaveDebug(page, "stake-typed", fullPage: false);
        log.LogInformation("1win: bet slip filled for {User} ({Count} picks, stake {Stake} {Typed}); left for the user to place", account.Username, picks.Count, stake, stakeTyped ? "typed" : "NOT typed");
        return stakeTyped;
    }

    /// <summary>Empties the bet slip: 1win keeps it in page storage, so the entry is removed and the page reloaded.</summary>
    private async Task ClearBetSlipAsync(IPage page)
    {
        await page.EvaluateAsync("key => localStorage.removeItem(key)", o.BetSlipStorageKey);
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(5_000);
        log.LogInformation("1win: bet slip emptied ({Count} selections left)", await page.Locator(o.BetSlipItem).CountAsync());
    }

    /// <summary>Opens the pick's match and block (Ref = url | tab | title | side | line), checks the odds and clicks the cell.</summary>
    private async Task AddPickAsync(IPage page, Pick pick)
    {
        var parts = pick.Selection.Ref.Split('|');
        var teams = $"{pick.Match.Home} v {pick.Match.Away}";
        var (tab, title, side) = (parts[1], parts[2], parts[3]);
        var line = decimal.Parse(parts[4], CultureInfo.InvariantCulture);

        await page.OpenAsync(parts[0], log);
        await page.WaitForTimeoutAsync(3_000);
        var titleEl = await FindBlockAsync(page, tab, title) ?? throw new InvalidOperationException($"'{title}' is no longer offered for {teams}.");

        var cells = await titleEl.EvaluateAsync<string[][]>(CellsScript, -1);
        int i = Array.FindIndex(cells, c =>
        {
            var m = Regex.Match(c[0], @"^(Under|Over)\s+(\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);
            return m.Success && m.Groups[1].Value.Equals(side, StringComparison.OrdinalIgnoreCase)
                   && decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) == line;
        });
        if (i < 0) throw new InvalidOperationException($"No '{side} {line}' in '{title}' for {teams}.");
        var (name, price, selected) = (cells[i][0], cells[i][1], cells[i][2]);
        // Never at odds outside the rules, even if they moved since the slip was built.
        var odds = decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : (decimal?)null;
        if (odds is null || odds < rules.MinPickOdds || odds > rules.MaxPickOdds)
            throw new InvalidOperationException($"Odds moved for {teams}: '{name}' in '{title}' is now {price}.");
        if (selected == "true") throw new InvalidOperationException($"'{name}' for {teams} was already in the bet slip.");

        await titleEl.EvaluateAsync<string[][]>(CellsScript, i); // marks the cell so it can be clicked like a person would
        var cell = page.Locator("[data-mikeka-pick='1']").First;
        await cell.ScrollIntoViewIfNeededAsync(new() { Timeout = 10_000 });
        await DismissPopupsAsync(page);
        await cell.ClickAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(1_500);
        if (!Regex.IsMatch(await cell.GetAttributeAsync("class") ?? "", "_selected_"))
            throw new InvalidOperationException($"Clicked '{name}' @ {price} for {teams} but it did not go into the bet slip.");
        await cell.EvaluateAsync("b => b.removeAttribute('data-mikeka-pick')");
        log.LogInformation("1win: bet slip has {Teams}: {Title}, {Name} @ {Odds}", teams, title, name, price);
    }

    /// <summary>
    /// From a block title, its Over/Under odds buttons as [name, price, selected] (button text "Under 9.5" + "1.69";
    /// a chosen one has a "_selected_" class). With mark ≥ 0 that button also gets data-mikeka-pick="1".
    /// </summary>
    private const string CellsScript = @"(title, mark) => {
        document.querySelectorAll('[data-mikeka-pick]').forEach(e => e.removeAttribute('data-mikeka-pick'));
        const isCell = b => /^(Under|Over) [\d.]+ [\d.]+$/.test(b.innerText.replace(/\s+/g, ' ').trim());
        let n = title;
        for (let i = 0; i < 8 && n.parentElement; i++) {
            n = n.parentElement;
            const cells = [...n.querySelectorAll('button')].filter(isCell);
            if (!cells.length) continue;
            if (mark >= 0 && cells[mark]) cells[mark].setAttribute('data-mikeka-pick', '1');
            return cells.map(b => {
                const t = b.innerText.replace(/\s+/g, ' ').trim().match(/^((?:Under|Over) [\d.]+) ([\d.]+)$/);
                return [t[1], t[2], /_selected_/.test(b.className) ? 'true' : ''];
            });
        }
        return [];
    }";

    /// <summary>
    /// The slip's card on 1win's Bet history page. A slip placed since 2026-10-09 has 1win's bet ID as its bet number:
    /// exactly that card is read. Older slips ("1win-yyyyMMdd-HHmmss") are matched by placing time, picks and odds, and
    /// only when exactly ONE card fits (two fitting cards = no guess, Pending); the card's ID is then kept on the slip.
    /// Not found = Pending, with a "history-not-found" screenshot.
    /// </summary>
    public async Task<BetOutcome> GetOutcomeAsync(Slip slip, CancellationToken ct)
    {
        var page = await Page();
        var cards = await ReadCardsAsync(page);
        HistoryCard? card;
        if (IsBetId(slip.BetReference))
        {
            card = cards.FirstOrDefault(c => c.Id == slip.BetReference);
        }
        else
        {
            var placedUtc = DateTime.TryParseExact(slip.BetReference?.Replace("1win-", ""), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var r) ? r : slip.CreatedAt;
            var fits = Fitting(cards, placedUtc, slip.Picks.Count, slip.CombinedOdds, 0.02m);
            if (fits.Count > 1)
            {
                var shot = await SaveDebug(page, "history-two-cards");
                log.LogWarning("1win: slip #{Id}: {Count} history cards fit its time, picks and odds (IDs {Ids}); not deciding (screenshot {Shot})",
                    slip.Id, fits.Count, string.Join(", ", fits.Select(c => c.Id)), shot);
                return BetOutcome.Pending;
            }
            card = fits.FirstOrDefault();
            if (card is not null)
            {
                log.LogInformation("1win: slip #{Id} is bet ID {BetId}; kept as its bet number", slip.Id, card.Id);
                slip.BetReference = card.Id; // saved with the run; next time exactly this card is read
            }
        }
        if (card is null)
        {
            var shot = await SaveDebug(page, "history-not-found");
            log.LogWarning("1win: slip #{Id} (bet {Ref}) not found in the bet history (screenshot {Shot})", slip.Id, slip.BetReference, shot);
            return BetOutcome.Pending;
        }
        log.LogInformation("1win: slip #{Id} in history → {Outcome}: {Text}", slip.Id, card.Outcome, card.Text);
        return card.Outcome;
    }

    /// <summary>1win's bet ID, e.g. "330497469" (older slips have "1win-yyyyMMdd-HHmmss").</summary>
    private static bool IsBetId(string? reference) => reference is { Length: >= 5 } && reference.All(char.IsDigit);

    private sealed record HistoryCard(string Id, DateTime PlacedEat, BetOutcome Outcome, decimal Odds, int Events, string Text);

    /// <summary>
    /// Each card on 1win's Bet history page (seen 2026-10-08), e.g.
    /// "7 October 2026 at 09:26 am ID 330497469 Opened • Multiple 1.81 5 events Bet / Possible win TZS 1,000.00 » TZS 1,866.70".
    /// The smallest element holding both the date line and "Bet /" is one card.
    /// </summary>
    private const string HistoryCardsScript = @"async () => {
        for (let i = 0; i < 3; i++) { window.scrollBy(0, window.innerHeight); await new Promise(r => setTimeout(r, 700)); }
        window.scrollTo(0, 0);
        const isCard = el => /\d{1,2} [A-Za-z]+ \d{4} at \d{1,2}:\d{2}\s*[ap]m/i.test(el.innerText || '') && /Bet\s*\//.test(el.innerText || '');
        const all = [...document.body.querySelectorAll('*')].filter(el => el.offsetParent !== null && isCard(el));
        return all.filter(el => ![...el.querySelectorAll('*')].some(c => all.includes(c))).map(el => el.innerText.replace(/\s+/g, ' ').trim());
    }";

    private static readonly Regex CardRegex = new(
        @"(?<date>\d{1,2} [A-Za-z]+ \d{4}) at (?<time>\d{1,2}:\d{2}\s*[ap]m).*?\bID (?<id>\d+)\s+(?<status>[A-Za-z ]+?)\s*•.*?(?<odds>\d+(\.\d+)?)\s+(?<events>\d+) events?",
        RegexOptions.IgnoreCase);

    /// <summary>Cards placed within 3 minutes of the time (UTC) with this many picks and odds within the tolerance.</summary>
    private static List<HistoryCard> Fitting(IEnumerable<HistoryCard> cards, DateTime placedUtc, int picks, decimal odds, decimal tolerance)
    {
        var placedEat = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(placedUtc, DateTimeKind.Utc), Eat.Zone);
        return cards.Where(c => Math.Abs((c.PlacedEat - placedEat).TotalMinutes) <= 3 && c.Events == picks
                                && Math.Abs(c.Odds - odds) <= tolerance).ToList();
    }

    /// <summary>The one card fitting a bet just placed, or null (none, or more than one).</summary>
    private async Task<HistoryCard?> FindCardAsync(IPage page, DateTime placedUtc, int picks, decimal odds, decimal tolerance)
    {
        var fits = Fitting(await ReadCardsAsync(page), placedUtc, picks, odds, tolerance);
        return fits.Count == 1 ? fits[0] : null;
    }

    /// <summary>Every card on the Bet history page (newest first, as 1win lists them). Its status gives the result: Lost / Opened / Won.</summary>
    private async Task<List<HistoryCard>> ReadCardsAsync(IPage page)
    {
        var result = new List<HistoryCard>();
        foreach (var path in o.HistoryPaths)
        {
            string[] cards = [];
            try
            {
                await page.OpenAsync(At(path), log);
                await page.WaitForTimeoutAsync(3_000);
                for (int attempt = 1; ; attempt++)
                {
                    try { cards = await page.EvaluateAsync<string[]>(HistoryCardsScript); break; }
                    catch (PlaywrightException ex) when (attempt < 3 && ex.Message.Contains("context was destroyed", StringComparison.OrdinalIgnoreCase))
                    {
                        await page.WaitForTimeoutAsync(3_000);
                    }
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                log.LogWarning("1win: bet history not read at {Path}: {Error}", path, ex.Message.Split('\n')[0]);
                continue;
            }
            foreach (var card in cards)
            {
                var m = CardRegex.Match(card);
                if (!m.Success) continue;
                if (!DateTime.TryParseExact($"{m.Groups["date"].Value} {m.Groups["time"].Value.Replace(" ", "")}", "d MMMM yyyy h:mmtt",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var cardTime)) continue;
                var status = m.Groups["status"].Value.Trim().ToLowerInvariant();
                var outcome = status.StartsWith("lost") ? BetOutcome.Lost
                    : status.StartsWith("won") || status.StartsWith("return") || status.StartsWith("cashed") ? BetOutcome.Won
                    : BetOutcome.Pending; // Opened (and anything unknown) = still running
                result.Add(new HistoryCard(m.Groups["id"].Value, cardTime, outcome,
                    decimal.Parse(m.Groups["odds"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["events"].Value), card));
            }
            if (result.Count > 0) break;
        }
        return result;
    }

    // ---------- helpers ----------

    private async Task Step(string name, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            var file = _page is null ? null : await SaveDebug(_page, name.Replace(' ', '-'));
            throw new InvalidOperationException($"1win step '{name}' failed: {ex.Message.Split('\n')[0]}" + (file is null ? "" : $" (screenshot: {file})"), ex);
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

    private static async Task<bool> IsVisible(ILocator l, int timeoutMs)
    {
        try { await l.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs }); return true; }
        catch (TimeoutException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        // Log out at the end of a run (bet placed, nothing to bet, or an error), unless a filled bet slip waits for the user.
        if (!browserTab.LeaveOpen && _page is { IsClosed: false } page)
        {
            await LogoutAsync();
            await ClearSiteDataAsync(page); // so the next 1win account can log in (also ends a session the logout missed)
        }
        await browserTab.DisposeAsync(); // the site's tab stays open for the next run (with the filled bet slip, if any)
    }

    /// <summary>
    /// Wipes what Chrome keeps for 1win only: its cookies (site and sub-domains), local/session storage, IndexedDB, service
    /// workers and cache storage, plus Chrome's HTTP cache (cache only — other sites stay logged in). Without this a second
    /// 1win account cannot log in after the first one logged out, until the browser cache is cleared by hand (seen 2026-10-07).
    /// </summary>
    private async Task ClearSiteDataAsync(IPage page)
    {
        var labels = _origin.Host.Split('.');
        var baseDomain = string.Join('.', labels.Skip(Math.Max(0, labels.Length - 2))); // 1wnorh.life
        try
        {
            // CDP calls have no timeout of their own: one that never answered (seen 2026-10-08) held the 1win tab for every account.
            var limit = TimeSpan.FromSeconds(20);
            var cdp = await page.Context.NewCDPSessionAsync(page).WaitAsync(limit);
            foreach (var origin in new[] { $"https://{baseDomain}", $"https://www.{baseDomain}", _origin.GetLeftPart(UriPartial.Authority) }.Distinct())
                await cdp.SendAsync("Storage.clearDataForOrigin", new Dictionary<string, object> { ["origin"] = origin, ["storageTypes"] = "all" }).WaitAsync(limit);
            await cdp.SendAsync("Network.clearBrowserCache").WaitAsync(limit);
            await cdp.DetachAsync().WaitAsync(limit);
            await page.Context.ClearCookiesAsync(new() { DomainRegex = new Regex(Regex.Escape(baseDomain) + "$", RegexOptions.IgnoreCase) });
            log.LogInformation("1win: site data cleared for {Domain}", baseDomain);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or System.TimeoutException)
        {
            log.LogWarning("1win: clearing site data failed: {Error}", ex.Message.Split('\n')[0]);
        }
    }

    /// <summary>
    /// Deletes Chrome's history entries for 1win (only those: the match pages read while choosing the slip, …) before
    /// logging in, as the user asked (2026-10-08). Done in a second tab on Chrome's History page with the page's own
    /// "select all" + "delete" (the same as clicking them), page by page; the tab is closed afterwards. Entries of any
    /// other site are never touched: if one is listed, it stops. A failure only writes a warning; the login goes on.
    /// </summary>
    private const string DeleteHistoryScript = @"async domain => {
        const sleep = ms => new Promise(r => setTimeout(r, ms));
        const app = document.querySelector('history-app');
        const list = () => app.shadowRoot.querySelector('history-list');
        const shown = () => [...list().shadowRoot.querySelectorAll('history-item')].filter(i => i.item);
        let removed = 0;
        for (let round = 0; round < 30; round++) {
            await sleep(1500);
            const all = shown();
            if (all.length === 0) break;
            if (all.some(i => !new URL(i.item.url).hostname.endsWith(domain))) return 'stopped (another site listed) after ' + removed;
            list().selectAllItems();
            list().deleteSelectedWithPrompt();
            await sleep(800);
            const ok = list().shadowRoot.querySelector('cr-dialog .action-button');
            if (!ok) return 'no confirm button after ' + removed;
            ok.click();
            removed += all.length;
        }
        return 'removed ' + removed;
    }";

    private async Task ClearHistoryAsync(IPage page)
    {
        var labels = _origin.Host.Split('.');
        var baseDomain = string.Join('.', labels.Skip(Math.Max(0, labels.Length - 2))); // 1wnorh.life
        IPage? historyTab = null;
        try
        {
            historyTab = await page.Context.NewPageAsync();
            await historyTab.GotoAsync($"chrome://history/?q={Uri.EscapeDataString(baseDomain)}", new() { Timeout = 20_000 });
            var result = await historyTab.EvaluateAsync<string>(DeleteHistoryScript, baseDomain).WaitAsync(TimeSpan.FromMinutes(2));
            log.LogInformation("1win: browser history for {Domain}: {Result}", baseDomain, result);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or System.TimeoutException)
        {
            log.LogWarning("1win: clearing browser history failed: {Error}", ex.Message.Split('\n')[0]);
        }
        finally
        {
            try { if (historyTab is { IsClosed: false }) await historyTab.CloseAsync(); } catch (PlaywrightException) { }
            try { await page.BringToFrontAsync(); } catch (PlaywrightException) { }
        }
    }

    /// <summary>
    /// Logs out at the end of a run: sidebar profile → Settings → "Log out" (bottom of the Settings window) → "Log out" again
    /// in the confirmation → the page reloads with the Login button (seen on the live site 2026-10-06).
    /// If the Login button does not come back, the account's Activity says so.
    /// </summary>
    private async Task LogoutAsync()
    {
        if (_page is null || _page.IsClosed) return;
        var page = _page;
        try
        {
            if (await page.Locator(o.LoginButton).First.IsVisibleAsync()) return; // not logged in
            await DismissPopupsAsync(page);
            var logout = page.Locator(o.LogoutButton).First;
            if (!await logout.IsVisibleAsync())
            {
                await page.Locator(o.ProfileButton).First.ClickAsync(new() { Timeout = 10_000 });
                await page.WaitForTimeoutAsync(1_500);
                await page.Locator(o.SettingsButton).First.ClickAsync(new() { Timeout = 10_000 });
                await page.WaitForTimeoutAsync(2_000);
            }
            await logout.ScrollIntoViewIfNeededAsync(new() { Timeout = 10_000 });
            await logout.ClickAsync(new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(1_500);
            // "Log out?" — the second Log out button, in the confirmation window.
            var confirm = page.Locator(o.LogoutConfirm).Filter(new() { HasTextRegex = new Regex(o.LogoutRegex, RegexOptions.IgnoreCase) })
                .Locator("visible=true").Last;
            if (await confirm.IsVisibleAsync())
            {
                try { await confirm.ClickAsync(new() { Timeout = 10_000, NoWaitAfter = true }); }
                catch (PlaywrightException) { } // the page reloads while logging out
            }
            if (await IsVisible(page.Locator(o.LoginButton).First, 30_000))
            {
                log.LogInformation("Logged out of {Site} ({User})", _origin.Host, account.Username);
                return;
            }
            var shot = await SaveDebug(page, "logout-not-done", fullPage: false);
            await browserTab.NoteAsync($"1win: Log out was clicked but {account.Username} still looks logged in (screenshot: {shot}). Please log out by hand in the 1win tab.");
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            var shot = await SaveDebug(page, "logout-failed", fullPage: false);
            await browserTab.NoteAsync($"1win: log out failed ({ex.Message.Split('\n')[0]}; screenshot: {shot}). Please log out by hand in the 1win tab.");
        }
    }
}
