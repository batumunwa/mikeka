using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Api.Bookmaker;

/// <summary>
/// Page selectors for coldbet1f.com ("Coldbet" section in appsettings.json).
/// The defaults are first guesses; they are checked and corrected against the live site step by step.
/// </summary>
public class ColdbetOptions
{
    public bool Headless { get; set; } = true;
    public int MaxMatchesToScan { get; set; } = 80;
    /// <summary>Folder for screenshots/HTML saved when a step fails, to fix selectors.</summary>
    public string DebugDir { get; set; } = "logs/coldbet-debug";

    /// <summary>Header area present whether logged in or not; used to know the page has finished drawing.</summary>
    public string HeaderControls { get; set; } = ".header-top__controls";
    public string LoginButton { get; set; } = "button.auth-dropdown-trigger";
    public string UsernameInput { get; set; } = "input#username";
    public string PasswordInput { get; set; } = "input#username-password";
    public string LoginSubmit { get; set; } = "button.auth-form-fields__submit";
    /// <summary>Element only shown when logged in (the header balance box). Empty = logged in when the Log in button is gone.</summary>
    public string LoggedInMarker { get; set; } = ".user-office__balance";
    /// <summary>Header "My account" menu; holds the Log out item.</summary>
    public string AccountMenu { get; set; } = "a.double-row-header-user-office-dropdown__trigger";
    public string LogoutButton { get; set; } = @"text=/^\s*(log ?out|sign ?out|exit)\s*$/i";
    /// <summary>Confirmation button if the site asks "Are you sure?" after Log out.</summary>
    public string LogoutConfirm { get; set; } = "button:has-text('Yes'), button:has-text('OK'), button:has-text('Log out')";
    public string Balance { get; set; } = "[data-gtm='account-balance-value-desktop']";

    public string FootballPath { get; set; } = "/en/line/football";
    /// <summary>Pseudo-matches the site lists inside leagues (aggregate statistics), skipped.</summary>
    public string[] SkipTeams { get; set; } = ["Home", "Away"];

    /// <summary>Loading placeholders shown until a match's markets have loaded.</summary>
    public string MarketsLoader { get; set; } = ".market-grid-loader";
    /// <summary>The "Regular time ▾" dropdown on a match page (its current value) and its options.</summary>
    public string PeriodDropdown { get; set; } = ".multiselect__single";
    public string PeriodOption { get; set; } = ".multiselect__content li";
    /// <summary>The clickable box of that dropdown.</summary>
    public string PeriodDropdownBox { get; set; } = ".multiselect";
    /// <summary>The market grid (drawn on a canvas); screenshots of it are read for the odds.</summary>
    public string MarketGrid { get; set; } = ".market-grid";
    public int MaxGridScreenshots { get; set; } = 5;
    /// <summary>Pause before the first grid screenshot: Coldbet draws the odds on a canvas after the page loads.</summary>
    public int GridDrawWaitMs { get; set; } = 2500;
    /// <summary>Pixels to scroll inside the grid between screenshots.</summary>
    public int GridScrollStep { get; set; } = 650;
    /// <summary>Most screenshots taken while looking for the cell to click.</summary>
    public int LocateMaxScreenshots { get; set; } = 10;
    /// <summary>While looking for the cell: scroll this share of the visible grid between screenshots (overlap keeps block titles in view).</summary>
    public double LocateScrollFraction { get; set; } = 0.6;
    /// <summary>BLOCK button of the "Show notifications" popup.</summary>
    public string NotificationsBlock { get; set; } = @"text=/^\s*block\s*$/i"; // whole text only: "Collapse block" must not match
    /// <summary>Where each market lives: dropdown option + exact block title (match total, not team totals or halves).</summary>
    public MarketSection[] MarketSections { get; set; } =
    [
        new() { Market = "goals", Option = "Regular time", Title = "Total", FeedSubGame = "" },
        new() { Market = "corners", Option = "Corners", Title = "Total. Corners", FeedSubGame = "Corners" },
        new() { Market = "cards", Option = "Yellow Cards", Title = "Total. Yellow Cards", FeedSubGame = "Yellow Cards" },
        new() { Market = "fouls", Option = "Fouls", Title = "Total. Fouls", FeedSubGame = "Fouls" }, // title not yet confirmed on the live site
    ];
    /// <summary>Filter tabs above a match's markets ("All markets", "Total", "Intervals", …).</summary>
    public string FilterTab { get; set; } = ".game-toolbar-filter-switch__name";
    /// <summary>Interval "nothing happens" row names: "16-30 Mins - No" or "Under 0.5 In 15 Minute".</summary>
    public string IntervalNameRegex { get; set; } = @"^\s*(\d+\s*-\s*\d+\s*Mins?\s*-\s*No|Under\s+\d+(?:\.\d+)?\s+In\s+\d+\s+Minutes?)\s*$";
    /// <summary>"16-30 Mins - No": group 1 = from, group 2 = to.</summary>
    public string IntervalRangeRegex { get; set; } = @"^\s*(\d+)\s*-\s*(\d+)\s*Mins?\s*-\s*No\s*$";
    /// <summary>"Under 0.5 In 15 Minute": group 1 = to (from kickoff, minute 1).</summary>
    public string IntervalFromKickoffRegex { get; set; } = @"^\s*Under\s+(\d+(?:\.\d+)?)\s+In\s+(\d+)\s+Minutes?\s*$";
    /// <summary>Row text in a Total block, e.g. "Under 9.5 1.15": group 1 = side, 2 = line, 3 = odds.</summary>
    public string TotalRowRegex { get; set; } = @"^\s*(Over|Under)\s+(\d+(?:[.,]\d+)?)\s+(\d+(?:[.,]\d+)?)\s*$";

    // Bet slip ("coupon") on the right; plain page text, unlike the odds grid. Seen on the live site on 2026-10-06.
    /// <summary>One selection in the bet slip: "280688. UEFA Nations League Kazakhstan - Faroe Islands 1.143 Total: Under 3.5".</summary>
    public string BetSlipItem { get; set; } = ".coupon-app__component .coupon-bet__container";
    /// <summary>In a selection: its market and outcome ("Total: Under 3.5") and its odds.</summary>
    public string BetSlipItemName { get; set; } = ".ui-coupon-bet-market__name";
    public string BetSlipItemOdds { get; set; } = ".ui-coupon-bet-market__coef";
    public string BetSlipTotalOdds { get; set; } = ".coupon-app__component .coupon-result-coef-value";
    /// <summary>"Single bet" / accumulator dropdown.</summary>
    public string BetSlipType { get; set; } = ".coupon-app__component .coupon-type-selector";
    /// <summary>The bet type wanted when the slip has more than one selection.</summary>
    public string AccumulatorRegex { get; set; } = @"accumulator|express|multi|parlay|combo";
    public string BetSlipStakeInput { get; set; } = ".coupon-app__component input.ui-number-input__field";
    /// <summary>The × that takes one selection out of the bet slip (inside a BetSlipItem).</summary>
    public string BetSlipItemRemove { get; set; } = ".coupon-bet-remove";
    public string BetSlipClear { get; set; } =".coupon-app__component button.coupon-delete-bets";
    /// <summary>CLEAR in the "Clear bet slip?" question that follows the bin button.</summary>
    /// (Its caption "Сlear" starts with a Cyrillic С, so it is found by its data-test, not by text.)
    public string BetSlipClearConfirm { get; set; } = "[data-test='ui-popup']:has-text('bet slip') [data-test='ui-popup-submit']";
    /// <summary>One-click betting switch: when on, a click on the odds places a bet at once, so the system refuses to bet.</summary>
    public string OneClickSwitch { get; set; } = ".coupon-one-click input[type='checkbox']";
    /// <summary>The bet slip's main button ("REGISTRATION" when logged out).</summary>
    public string BetSlipPlaceButton { get; set; } = ".coupon-app__component .coupon-buttons button";
    /// <summary>The button text must match this before it is clicked.</summary>
    public string PlaceButtonTextRegex { get; set; } = @"^\s*(place|make)\s+(a\s+)?bet\s*$|^\s*bet\s*$";
    /// <summary>Where a confirmation with the bet number may appear after placing (bet slip, pop-ups).</summary>
    public string BetConfirmation { get; set; } = ".coupon-app__component, [role='dialog'], [class*='modal' i], [class*='popup' i], [class*='notification' i]";
    /// <summary>Bet number in the confirmation text; group 1 = the number. Needs a word like "bet" before it (event codes are numbers too).</summary>
    public string BetIdRegex { get; set; } = @"\b(?:bet(?:\s*slip)?|coupon|ticket)\b\s*(?:no\.?|number|№|#|id)?\s*[:#№]?\s*(\d{6,})"; // Coldbet: "Bet slip № 88383024337"
    public int ConfirmationWaitSeconds { get; set; } = 30;

    public string HistoryPath { get; set; } = "/en/office/history";
    public string HistoryRow { get; set; } = "[class*='history' i] [class*='row' i], [class*='bet-item' i]";
    public string HistoryWonRegex { get; set; } = @"\bwon\b|\bwin\b";
    public string HistoryLostRegex { get; set; } = @"\blost\b|\bloss\b";
}

public class MarketSection
{
    public string Market { get; set; } = "";
    public string Option { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Whole-match sub-market in Coldbet's odds data ("Corners"); empty = the match's own markets.</summary>
    public string FeedSubGame { get; set; } = "";
}

public class ColdbetFactory(IOptions<ColdbetOptions> options, SharedBrowser browser, IOptions<BettingRules> rules, ScreenshotOddsReader reader, ILogger<ColdbetClient> log) : IBookmakerFactory
{
    public async Task<IBookmakerClient> CreateAsync(Account account, string password, CancellationToken ct)
    {
        var o = options.Value;
        var tab = await browser.OpenTabAsync(account, o.LoginButton, ct);
        return new ColdbetClient(tab, account, password, o, rules.Value, reader, log);
    }
}

public sealed class ColdbetClient(
    BrowserTab browserTab, Account account, string password,
    ColdbetOptions o, BettingRules rules, ScreenshotOddsReader reader, ILogger log) : IBookmakerClient
{
    private readonly Uri _origin = new(new Uri(account.Url).GetLeftPart(UriPartial.Authority));
    private IPage? _page;
    private bool _loggedIn;
    private Task<IPage> Page() => Task.FromResult(_page ??= browserTab.Page);
    private string At(string path) => new Uri(_origin, path).ToString();

    public async Task LoginAsync(CancellationToken ct)
    {
        var page = await Page();
        await Step("open site", async () =>
            await page.OpenAsync(account.Url, log));
        // The site's cookies are cleared when the run's tab opens (SharedBrowser), so this account always logs in with its own credentials.

        await Step("login", async () =>
        {
            var username = page.Locator(o.UsernameInput).First;
            if (!await IsVisible(username, 1_000))
                await page.Locator(o.LoginButton).First.ClickAsync(); // open the login dropdown
            await username.FillAsync(account.Username, new() { Timeout = 15_000 });
            await page.Locator(o.PasswordInput).First.FillAsync(password, new() { Timeout = 15_000 });
            await page.Locator(o.LoginSubmit).First.ClickAsync(new() { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(4_000);
            await SaveDebug(page, "after-submit", fullPage: false);
            // The header redraws after submit, so check twice with a pause in between.
            if (!await IsLoggedIn(page)) throw new TimeoutException("Still logged out after submitting the login form (see after-submit screenshot).");
            await page.WaitForTimeoutAsync(3_000);
            if (!await IsLoggedIn(page)) throw new TimeoutException("Login did not stick: the Log in button came back.");
        });
        _loggedIn = true;
        log.LogInformation("Logged in to {Site} as {User}", _origin.Host, account.Username);
    }

    public async Task<decimal> GetBalanceAsync(CancellationToken ct)
    {
        var page = await Page();
        string text = "";
        await Step("read balance", async () => text = await page.Locator(o.Balance).First.InnerTextAsync(new() { Timeout = 15_000 }));
        return ParseNumber(text) ?? throw new InvalidOperationException($"Could not read a balance from '{text}'");
    }

    public async Task<IReadOnlyList<MatchInfo>> GetMatchesAsync(IReadOnlyCollection<string> leagues, IReadOnlyList<MarketChoice> markets, DateTime untilUtc, CancellationToken ct)
    {
        var page = await Page();
        var listed = new List<(string url, string league, string home, string away, DateTime kickoff, ColdbetFeed.Game game)>();

        // 1) Leagues and matches from Coldbet's own odds data (plain JSON; the page draws the same data on a canvas).
        // The data is fetched from inside a Coldbet page (the analysis may start on a blank tab).
        if (!page.Url.StartsWith(_origin.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            await Step("open football list", () => page.OpenAsync(At(o.FootballPath), log));
        List<ColdbetFeed.League> siteLeagues = [];
        await Step("read leagues", async () => siteLeagues = await ColdbetFeed.LeaguesAsync(page));
        foreach (var league in leagues)
        {
            ct.ThrowIfCancellationRequested();
            var found = siteLeagues.FirstOrDefault(l => ColdbetFeed.SameLeague(l.Name, league));
            if (found is null)
            {
                log.LogWarning("Coldbet: league '{League}' not offered now (or the name differs from the site's)", league);
                continue;
            }
            List<ColdbetFeed.Game> games = [];
            await Step($"read matches of {league}", async () => games = await ColdbetFeed.GamesAsync(page, found.Id));
            int before = listed.Count;
            foreach (var g in games.Take(o.MaxMatchesToScan))
            {
                // Skip "Home v Away" / "Home (Special bets)" rows: placeholders for special bets, not matches.
                if (g.PageId == 0 || o.SkipTeams.Any(t => g.Home.StartsWith(t, StringComparison.OrdinalIgnoreCase) && g.Away.StartsWith("Away", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (g.KickoffUtc > untilUtc) continue;
                // The match page: /en/line/football/1706694-uefa-nations-league/375893614-kazakhstan-faroe-islands
                var url = new Uri(_origin, $"{o.FootballPath}/{found.Id}-{ColdbetFeed.Slug(found.Name)}/{g.PageId}-{ColdbetFeed.Slug(g.Home + " " + g.Away)}").ToString();
                listed.Add((url, league, g.Home, g.Away, g.KickoffUtc, g));
            }
            log.LogInformation("Coldbet: {Count} matches in {League} before {Until:yyyy-MM-dd HH:mm} UTC", listed.Count - before, league, untilUtc);
        }

        // 2) Each match: for each of the account's choices in order, the whole-match Under lines from the odds data, or the
        //    interval "No" rows from screenshots of the match page (intervals are not mapped in the data yet).
        var result = new List<MatchInfo>();
        foreach (var m in listed)
        {
            ct.ThrowIfCancellationRequested();
            var sels = new List<Selection>();
            var kickoff = m.kickoff;
            try
            {
                bool pageOpen = false;
                var readTotals = new HashSet<string>();
                var readIntervals = new HashSet<string>();
                foreach (var choice in markets.Where(c => c.Leagues.Any(l => l.Trim().Equals(m.league.Trim(), StringComparison.OrdinalIgnoreCase))))
                {
                    var section = o.MarketSections.FirstOrDefault(s => s.Market.Equals(choice.Market, StringComparison.OrdinalIgnoreCase));
                    if (section is null) { log.LogWarning("Coldbet: no page settings for market '{Market}'", choice.Market); continue; }

                    if (choice.IntervalKey is null && readTotals.Add(section.Market))
                    {
                        // The match itself (goals) or its whole-match sub-market (no period: not "1st half").
                        long? id = section.FeedSubGame.Length == 0 ? m.game.Id
                            : m.game.SubGames.FirstOrDefault(s => s.Name.Equals(section.FeedSubGame, StringComparison.OrdinalIgnoreCase) && s.Period.Length == 0)?.Id;
                        if (id is null) continue;
                        foreach (var (side, line, odds) in await ColdbetFeed.TotalsAsync(page, id.Value))
                            // Ref = match url | dropdown option | block title | side | line: enough to find and click it again.
                            sels.Add(new Selection(section.Market, side, line, odds,
                                $"{m.url}|{section.Option}|{section.Title}|{side}|{line.ToString(CultureInfo.InvariantCulture)}"));
                    }
                    else if (choice.IntervalKey is not null && readIntervals.Add(section.Market))
                    {
                        if (!pageOpen) { await OpenMatchAsync(page, m.url); pageOpen = true; }
                        foreach (var (interval, line, odds, name) in await ReadIntervalRowsAsync(page, section))
                            // Ref = match url | dropdown option | INTERVAL | Under | exact row name (e.g. "16-30 Mins - No").
                            sels.Add(new Selection(section.Market, "Under", line, odds,
                                $"{m.url}|{section.Option}|INTERVAL|Under|{name}", interval));
                    }

                    // The slip builder takes the first usable choice, so later ones need not be read for this match.
                    if (sels.Any(x => x.Market == section.Market && x.Side == choice.Side && x.Interval == choice.IntervalKey
                                      && (choice.RequiredLine is not { } want || x.Line == want)
                                      && x.Odds >= rules.MinPickOdds && x.Odds <= rules.MaxPickOdds))
                        break;
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException)
            {
                log.LogWarning("Could not read markets for {Home} v {Away}: {Error}", m.home, m.away, ex.Message.Split('\n')[0]);
                await SaveDebug(page, "markets-failed", fullPage: false);
            }
            log.LogInformation("Coldbet: {Home} v {Away}: {Summary}", m.home, m.away,
                sels.Count == 0 ? "no total lines" : string.Join(", ", sels.GroupBy(x => x.Market).Select(g => $"{g.Count()} {g.Key} lines")));
            // Matches without cards/corners selections are still returned; the slip builder skips them.
            result.Add(new MatchInfo(m.url, m.league, m.home, m.away, kickoff, sels));
        }
        log.LogInformation("Coldbet: {Listed} matches listed, {WithMarkets} with cards/corners totals",
            listed.Count, result.Count(r => r.Selections.Count > 0));
        return result;
    }

    /// <summary>
    /// Puts the picks in the bet slip and places them as one accumulator. Coldbet draws its odds on a canvas, so each cell is
    /// found in a screenshot and clicked by position; the bet slip (plain text) is then checked selection by selection.
    /// Anything unexpected before "Place" empties the bet slip and throws: nothing is placed.
    /// Once "Place" is clicked, a missing confirmation throws <see cref="BetUnconfirmedException"/>.
    /// </summary>
    public async Task<string> PlaceSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
    {
        var page = await Page();
        var label = await FillAndCheckAsync(page, picks, stake, ct);

        // From here the bet may exist: every failure is "unconfirmed", never "not placed".
        try
        {
            await page.Locator(o.BetSlipPlaceButton).First.ClickAsync(new() { Timeout = 10_000 });
            var id = await WaitForBetIdAsync(page);
            var shot = await SaveDebug(page, "after-place", fullPage: false);
            if (id is null)
                throw new BetUnconfirmedException(
                    $"'{label}' was clicked ({picks.Count} picks, stake {stake:N0}) but no bet number appeared within {o.ConfirmationWaitSeconds}s " +
                    $"(screenshot: {shot}). Check 'My bets' on Coldbet.");
            log.LogInformation("Coldbet: placed bet {Id} for {User}: {Count} picks, stake {Stake}", id, account.Username, picks.Count, stake);
            return id;
        }
        catch (Exception ex) when (ex is not BetUnconfirmedException)
        {
            var shot = await SaveDebug(page, "place-failed", fullPage: false);
            throw new BetUnconfirmedException($"Error after clicking '{label}': {ex.Message.Split('\n')[0]} (screenshot: {shot}). Check 'My bets' on Coldbet.", ex);
        }
    }

    /// <summary>Same checks as <see cref="PlaceSlipAsync"/> up to the Place button, which is left for the user; the tab stays open.</summary>
    public async Task<bool> FillSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
    {
        var page = await Page();
        await FillAndCheckAsync(page, picks, stake, ct);
        await page.BringToFrontAsync();
        browserTab.LeaveOpen = true;
        log.LogInformation("Coldbet: bet slip filled for {User} ({Count} picks, stake {Stake}); left for the user to place", account.Username, picks.Count, stake);
        return true; // FillAndCheckAsync typed and checked the stake (it throws otherwise)
    }

    /// <summary>Empties the bet slip, adds every pick, types the stake and checks it all. On any error the slip is emptied again.</summary>
    private async Task<string> FillAndCheckAsync(IPage page, IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct)
    {
        string label = "";
        try
        {
            await Step("fill bet slip", async () =>
            {
                var oneClick = page.Locator(o.OneClickSwitch).First;
                if (await oneClick.CountAsync() > 0 && await oneClick.IsCheckedAsync())
                    throw new InvalidOperationException("One-click betting is ON for this Coldbet account: a click on the odds would place a bet at once. Turn it off on the site first.");
                await ClearBetSlipAsync(page);
                foreach (var pick in picks)
                {
                    ct.ThrowIfCancellationRequested();
                    await AddPickAsync(page, pick, ct);
                }
                label = await CheckBetSlipAsync(page, picks, stake);
            });
        }
        catch
        {
            try { await ClearBetSlipAsync(page); } catch (Exception ex) { log.LogWarning("Could not empty the Coldbet bet slip: {Error}", ex.Message); }
            throw;
        }
        return label;
    }

    /// <summary>A click added another line of the right match (the grid moved before the click); it was taken out of the slip again.</summary>
    private sealed class WrongCellException(string message) : Exception(message);

    /// <summary>Finds the pick's cell in screenshots of the market grid, clicks it, and checks the bet slip shows it.</summary>
    private async Task AddPickAsync(IPage page, Pick pick, CancellationToken ct)
    {
        try { await AddPickOnceAsync(page, pick, ct); }
        catch (WrongCellException ex)
        {
            log.LogInformation("Coldbet: {Error} Removed it, trying once more.", ex.Message);
            try { await AddPickOnceAsync(page, pick, ct); }
            catch (WrongCellException again) { throw new InvalidOperationException(again.Message); }
        }
    }

    private async Task AddPickOnceAsync(IPage page, Pick pick, CancellationToken ct)
    {
        // Ref = match url | dropdown option | block title | side | line, or for intervals
        //       match url | dropdown option | INTERVAL | Under | "{from}-{to} Under {line}" (see GetMatchesAsync).
        var parts = pick.Selection.Ref.Split('|');
        var teams = $"{pick.Match.Home} v {pick.Match.Away}";
        var section = new MarketSection { Market = pick.Selection.Market, Option = parts[1], Title = parts[2] };
        var line = pick.Selection.Line.ToString("0.##", CultureInfo.InvariantCulture);
        await OpenMatchAsync(page, parts[0]);
        if (!await SwitchSectionAsync(page, section))
            throw new InvalidOperationException($"'{parts[1]}' markets are no longer offered for {teams}.");

        string target;
        if (parts[2] == "INTERVAL")
        {
            var tab = page.Locator(o.FilterTab).Filter(new() { HasTextRegex = new Regex(@"^\s*Intervals\s*$", RegexOptions.IgnoreCase) }).First;
            if (!await IsVisible(tab, 3_000)) throw new InvalidOperationException($"No Intervals tab for {teams}.");
            await tab.ClickAsync();
            await page.WaitForTimeoutAsync(2_000);
            var (from, to) = IntervalBounds(pick.Selection.Interval!);
            target = pick.Selection.Line == 0.5m && from > 1
                ? $"the cell labelled \"{from}-{to} Mins - No\" (whole match, not a team)"
                : pick.Selection.Line == 0.5m
                ? $"the cell labelled \"{from}-{to} Mins - No\" or \"Under 0.5 In {to} Minute\" (whole match, not a team)"
                : $"the cell labelled \"Under {line} In {to} Minute\" (whole match, not a team)";
        }
        else target = $"the cell labelled \"{parts[3]} {line}\" in the market block titled exactly \"{parts[2]}\"";

        await DismissNotificationsPopupAsync(page);
        var grid = page.Locator(o.MarketGrid).First;
        await grid.ScrollIntoViewIfNeededAsync(new() { Timeout = 10_000 });
        // Start at the very top of the grid, just below the sticky bar: the grid keeps its own scroll position from earlier reads,
        // and starting part-way down a block hides the block's title.
        var scrollers = await grid.EvaluateAsync<int>(GridToTopJs, await page.EvaluateAsync<float>(StickyTopBarBottomJs));
        log.LogDebug("Coldbet: grid scrolled to top ({Scrollers} inner scroll areas reset)", scrollers);
        await page.WaitForTimeoutAsync(o.GridDrawWaitMs); // the odds are drawn on a canvas after the page loads: let it finish
        CellLocation? cell = null;
        (float x, float y) click = default;
        (float x, float y, float width, float height) clip = default;
        var shots = new List<byte[]>();
        for (int i = 0; i <= o.LocateMaxScreenshots && cell is null; i++)
        {
            var box = await grid.BoundingBoxAsync() ?? throw new InvalidOperationException($"Market grid not shown for {teams}.");
            // Only the part of the grid inside the window and below the site's sticky top bar (it hides block titles):
            // a click must land on what the picture shows.
            var viewHeight = page.ViewportSize?.Height ?? await page.EvaluateAsync<int>("() => window.innerHeight"); // your own Chrome: real window size
            var barBottom = await page.EvaluateAsync<float>(StickyTopBarBottomJs);
            if (i > 0)
            {
                // Overlapping steps, so a block's title and its lower cells are in the same or consecutive pictures.
                var visible = viewHeight - Math.Max(box.Y, barBottom);
                await page.Mouse.MoveAsync(box.X + box.Width / 2, Math.Max(box.Y, barBottom) + Math.Min(visible / 2, 300));
                await page.Mouse.WheelAsync(0, Math.Max(150, (int)(visible * o.LocateScrollFraction)));
                await page.WaitForTimeoutAsync(800);
                box = await grid.BoundingBoxAsync() ?? box;
                barBottom = await page.EvaluateAsync<float>(StickyTopBarBottomJs);
            }
            await DismissNotificationsPopupAsync(page); // it can appear late and cover the grid
            float top = Math.Max(box.Y, barBottom), bottom = Math.Min(box.Y + box.Height, viewHeight);
            if (bottom - top < 50) continue;
            var shot = await page.ScreenshotAsync(new() { Clip = new() { X = box.X, Y = top, Width = box.Width, Height = bottom - top } });
            if (shots.Count > 0 && shot.AsSpan().SequenceEqual(shots[^1])) break; // reached the bottom
            cell = await reader.LocateCellAsync(shot, shots.Count > 0 ? shots[^1] : null, target, ct);
            shots.Add(shot);
            // The picture has device pixels (Windows display scaling, e.g. 125%), the mouse works in page pixels.
            var scale = ScreenshotOddsReader.PngSize(shot).width / box.Width;
            if (cell is not null) (click, clip) = ((box.X + (float)(cell.X / scale), top + (float)(cell.Y / scale)), (box.X, top, box.Width, bottom - top));
        }
        // The grid can still move (late drawing, a block opening above): only click where a fresh picture looks the same.
        var above = shots.Count > 1 ? shots[^2] : null; // the picture above the one the cell was found in
        for (int check = 0; cell is not null && check < 3; check++)
        {
            await page.WaitForTimeoutAsync(700);
            var fresh = await page.ScreenshotAsync(new() { Clip = new() { X = clip.x, Y = clip.y, Width = clip.width, Height = clip.height } });
            if (fresh.AsSpan().SequenceEqual(shots[^1])) break;
            log.LogInformation("Coldbet: grid changed before clicking for {Teams}, reading it again", teams);
            cell = await reader.LocateCellAsync(fresh, above, target, ct);
            shots.Add(fresh);
            var scale = ScreenshotOddsReader.PngSize(fresh).width / clip.width;
            if (cell is not null) click = (clip.x + (float)(cell.X / scale), clip.y + (float)(cell.Y / scale));
        }
        if (cell is null)
        {
            // Keep what the reader looked at, to see why (grid not drawn yet, block further down, …).
            string? first = null;
            try
            {
                Directory.CreateDirectory(o.DebugDir);
                var stem = Path.Combine(o.DebugDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-not-found");
                for (int s = 0; s < shots.Count; s++) await File.WriteAllBytesAsync($"{stem}-{s + 1}.png", shots[s], ct);
                first = shots.Count > 0 ? $"{stem}-1.png" : null;
            }
            catch (IOException) { }
            throw new InvalidOperationException($"Could not find {target} for {teams} in {shots.Count} screenshots: no longer offered or not readable" +
                                                (first is null ? "." : $" (screenshots: {first} …)."));
        }
        // Never bet at odds outside the rules, even if they moved since the slip was built.
        if (cell.Odds < rules.MinPickOdds || cell.Odds > rules.MaxPickOdds)
            throw new InvalidOperationException($"Odds moved for {teams}: {cell.Label} is now {cell.Odds}.");

        // The bet slip is real text: check the click added exactly this pick. The canvas ignores an instant
        // press+release and the slip can take a few seconds to update, so: a human-like click, a long wait, one retry.
        ILocator? item = null;
        for (int attempt = 1; attempt <= 2 && item is null; attempt++)
        {
            await page.Mouse.MoveAsync(click.x, click.y, new() { Steps = 5 });
            await page.WaitForTimeoutAsync(300);
            await page.Mouse.DownAsync();
            await page.WaitForTimeoutAsync(120);
            await page.Mouse.UpAsync();
            for (int wait = 0; wait < 16 && item is null; wait++)
            {
                await page.WaitForTimeoutAsync(500);
                item = await FindSlipItemAsync(page, pick);
            }
            if (item is null && attempt == 1) log.LogInformation("Coldbet: click on '{Label}' for {Teams} not in the bet slip after 8 s, clicking again", cell.Label, teams);
        }
        if (item is null)
        {
            var missed = await SaveDebug(page, "click-missed", fullPage: false);
            throw new InvalidOperationException($"Clicked '{cell.Label}' for {teams} at ({click.x:0}, {click.y:0}) but the match did not appear in the bet slip (screenshot: {missed}).");
        }
        var name = Regex.Replace(await item.Locator(o.BetSlipItemName).First.TextContentAsync() ?? "", @"\s+", " ").Trim();
        var odds = ParseNumber(await item.Locator(o.BetSlipItemOdds).First.TextContentAsync() ?? "");
        if (!SlipNameMatches(pick.Selection, name))
        {
            var wrong = $"The bet slip shows '{name}' for {teams}, not {SlipBuilder.SelectionText(pick.Selection)}.";
            await SaveDebug(page, "wrong-cell", fullPage: false);
            // Take the wrong line out again (its ×), so a retry or the final check sees a clean slip.
            await item.Locator(o.BetSlipItemRemove).First.ClickAsync(new() { Timeout = 10_000 });
            for (int i = 0; i < 10 && await FindSlipItemAsync(page, pick) is not null; i++) await page.WaitForTimeoutAsync(500);
            if (await FindSlipItemAsync(page, pick) is not null) throw new InvalidOperationException(wrong + " It could not be removed.");
            throw new WrongCellException(wrong);
        }
        if (odds is null || odds < rules.MinPickOdds || odds > rules.MaxPickOdds)
            throw new InvalidOperationException($"The bet slip shows odds {odds} for {teams}, outside {rules.MinPickOdds}–{rules.MaxPickOdds}.");
        log.LogInformation("Coldbet: bet slip has {Teams}: {Name} @ {Odds}", teams, name, odds);
    }

    /// <summary>
    /// Last check before placing: exactly these selections, accumulator, the stake typed in, combined odds in range.
    /// Returns the text of the Place button, which must read like "Place a bet".
    /// </summary>
    private async Task<string> CheckBetSlipAsync(IPage page, IReadOnlyList<Pick> picks, decimal stake)
    {
        await OpenBetSlipAsync(page);
        var count = await page.Locator(o.BetSlipItem).CountAsync();
        if (count != picks.Count) throw new InvalidOperationException($"The bet slip has {count} selections instead of {picks.Count}.");
        foreach (var pick in picks)
            if (await FindSlipItemAsync(page, pick) is null)
                throw new InvalidOperationException($"{pick.Match.Home} v {pick.Match.Away} is missing from the bet slip.");

        if (picks.Count > 1) await ChooseAccumulatorAsync(page);

        var input = page.Locator(o.BetSlipStakeInput).First;
        await input.FillAsync(stake.ToString("0", CultureInfo.InvariantCulture), new() { Timeout = 10_000 });
        await input.PressAsync("Tab");
        await page.WaitForTimeoutAsync(1_000);
        var typed = ParseNumber(await input.InputValueAsync());
        if (typed != stake) throw new InvalidOperationException($"The stake box shows {typed} instead of {stake:0}.");

        var total = ParseNumber(await page.Locator(o.BetSlipTotalOdds).First.TextContentAsync() ?? "");
        if (total is null || total < rules.MinCombinedOdds || total > rules.MaxCombinedOdds)
            throw new InvalidOperationException($"The bet slip's overall odds are {total}, outside {rules.MinCombinedOdds}–{rules.MaxCombinedOdds}.");

        var label = Regex.Replace(await page.Locator(o.BetSlipPlaceButton).First.InnerTextAsync(new() { Timeout = 10_000 }), @"\s+", " ").Trim();
        if (!Regex.IsMatch(label, o.PlaceButtonTextRegex, RegexOptions.IgnoreCase))
            throw new InvalidOperationException($"The bet slip button says '{label}', not 'Place a bet'. (Logged out? Set Coldbet:PlaceButtonTextRegex if the site's wording differs.)");
        await SaveDebug(page, "before-place", fullPage: false);
        log.LogInformation("Coldbet: bet slip ready for {User}: {Count} picks @ {Total}, stake {Stake}", account.Username, count, total, stake);
        return label;
    }

    /// <summary>With two or more selections the bet type must be an accumulator, never several single bets.</summary>
    private async Task ChooseAccumulatorAsync(IPage page)
    {
        var type = page.Locator(o.BetSlipType).First;
        var wanted = new Regex(o.AccumulatorRegex, RegexOptions.IgnoreCase);
        if (wanted.IsMatch(await type.InnerTextAsync(new() { Timeout = 10_000 }))) return;
        await type.ClickAsync(new() { Timeout = 10_000 });
        var option = page.GetByText(wanted).Locator("visible=true").First;
        if (await IsVisible(option, 5_000)) { await option.ClickAsync(); await page.WaitForTimeoutAsync(1_000); }
        var now = await type.InnerTextAsync();
        if (!wanted.IsMatch(now)) throw new InvalidOperationException($"The bet type is '{now.Trim()}', not an accumulator.");
    }

    /// <summary>The bet slip item for this pick's match (its text names both teams), or null.</summary>
    private async Task<ILocator?> FindSlipItemAsync(IPage page, Pick pick)
    {
        foreach (var item in await page.Locator(o.BetSlipItem).AllAsync())
        {
            var text = Regex.Replace(await item.TextContentAsync() ?? "", @"\s+", " ");
            if (text.Contains(pick.Match.Home, StringComparison.OrdinalIgnoreCase) && text.Contains(pick.Match.Away, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    /// <summary>
    /// Whether the bet slip's market text is this selection, e.g. "Total: Under 3.5" (goals), "Total. Corners: Under 9.5",
    /// "16-30 Mins - No", "Under 0.5 In 10 Minute". Team totals ("Total 1") and Over never match.
    /// </summary>
    public static bool SlipNameMatches(Selection sel, string name)
    {
        var line = Regex.Escape(sel.Line.ToString("0.##", CultureInfo.InvariantCulture));
        if (Regex.IsMatch(name, @"\bOver\b|\bYes\b|^\s*Total\s*[12]\b", RegexOptions.IgnoreCase)) return false;
        if (sel.Interval is { } interval)
        {
            var (from, to) = IntervalBounds(interval);
            return (sel.Line == 0.5m && Regex.IsMatch(name, $@"\b{from}\s*-\s*{to}\b.*\bNo\b", RegexOptions.IgnoreCase))
                || (from == 1 && Regex.IsMatch(name, $@"\bUnder\s+{line}\s+In\s+{to}\b", RegexOptions.IgnoreCase));
        }
        if (!Regex.IsMatch(name, $@"\bUnder\s+{line}(?![\d.])", RegexOptions.IgnoreCase)) return false;
        return sel.Market switch
        {
            "goals" => Regex.IsMatch(name, @"^\s*Total\s*:", RegexOptions.IgnoreCase),
            "corners" => name.Contains("Corner", StringComparison.OrdinalIgnoreCase),
            "cards" => name.Contains("Card", StringComparison.OrdinalIgnoreCase),
            "fouls" => name.Contains("Foul", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static (int from, int to) IntervalBounds(string interval)
    {
        var p = interval.Split('-');
        return (int.Parse(p[0]), int.Parse(p[1]));
    }

    /// <summary>Opens the bet slip panel if it is folded away (its stake box hidden).</summary>
    private async Task OpenBetSlipAsync(IPage page)
    {
        if (await IsVisible(page.Locator(o.BetSlipStakeInput).First, 1_000)) return;
        var tab = page.GetByText(new Regex(@"^\s*Bet slip\s*\d*\s*$", RegexOptions.IgnoreCase)).Locator("visible=true").First;
        if (await IsVisible(tab, 2_000)) await tab.ClickAsync();
        if (!await IsVisible(page.Locator(o.BetSlipStakeInput).First, 5_000))
            throw new InvalidOperationException("The bet slip panel is not showing.");
    }

    /// <summary>Removes every selection from the bet slip and checks it is empty.</summary>
    private async Task ClearBetSlipAsync(IPage page)
    {
        var items = page.Locator(o.BetSlipItem);
        if (await items.CountAsync() == 0) return;
        await OpenBetSlipAsync(page);
        await page.Locator(o.BetSlipClear).First.ClickAsync(new() { Timeout = 10_000 });
        var confirm = page.Locator(o.BetSlipClearConfirm).Locator("visible=true").First;
        if (await IsVisible(confirm, 3_000)) await confirm.ClickAsync(new() { Timeout = 5_000 });
        for (int i = 0; i < 10 && await items.CountAsync() > 0; i++) await page.WaitForTimeoutAsync(500);
        if (await items.CountAsync() > 0) throw new InvalidOperationException("Could not empty the bet slip.");
    }

    /// <summary>Waits for a bet number in the bet slip or a pop-up after "Place" was clicked. Null if none appears.</summary>
    private async Task<string?> WaitForBetIdAsync(IPage page)
    {
        var deadline = DateTime.UtcNow.AddSeconds(o.ConfirmationWaitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await page.WaitForTimeoutAsync(1_000);
            foreach (var el in await page.Locator(o.BetConfirmation).AllAsync())
            {
                var m = Regex.Match(await el.TextContentAsync() ?? "", o.BetIdRegex, RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value;
            }
        }
        return null;
    }

    public async Task<BetOutcome> GetOutcomeAsync(string betReference, CancellationToken ct)
    {
        var page = await Page();
        await page.OpenAsync(At(o.HistoryPath), log, WaitUntilState.NetworkIdle);
        var row = page.Locator(o.HistoryRow).Filter(new() { HasText = betReference }).First;
        if (await row.CountAsync() == 0) { await SaveDebug(page, "history-bet-not-found"); return BetOutcome.Pending; }
        var text = await row.InnerTextAsync();
        if (Regex.IsMatch(text, o.HistoryWonRegex, RegexOptions.IgnoreCase)) return BetOutcome.Won;
        if (Regex.IsMatch(text, o.HistoryLostRegex, RegexOptions.IgnoreCase)) return BetOutcome.Lost;
        return BetOutcome.Pending;
    }

    /// <summary>Runs a step; on failure saves a screenshot + HTML so the selector can be corrected.</summary>
    private async Task Step(string name, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            var file = _page is null ? null : await SaveDebug(_page, name.Replace(' ', '-'));
            throw new InvalidOperationException($"Coldbet step '{name}' failed: {ex.Message.Split('\n')[0]}" +
                                                (file is null ? "" : $" (screenshot: {file})"), ex);
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

    /// <summary>Waits for the header to render, then: logged in = marker visible, or (no marker set) Log in button absent.</summary>
    private async Task<bool> IsLoggedIn(IPage page)
    {
        // The header controls exist logged in or out; wait for them so a slow page is not mistaken for "logged in".
        var header = await IsVisible(page.Locator(o.HeaderControls).First, 45_000);
        log.LogDebug("Coldbet IsLoggedIn: header visible={Header}", header);
        if (!header) throw new TimeoutException("Coldbet header did not load.");
        if (!string.IsNullOrEmpty(o.LoggedInMarker))
            return await IsVisible(page.Locator(o.LoggedInMarker).First, 15_000); // balance widget loads after the header
        var loginVisible = await IsVisible(page.Locator(o.LoginButton).First, 20_000); // login widget loads after the header
        log.LogDebug("Coldbet IsLoggedIn: login button count={Count}, visible={Visible}",
            await page.Locator(o.LoginButton).CountAsync(), loginVisible);
        return !loginVisible;
    }

    private static async Task<bool> IsVisible(ILocator l, int timeoutMs)
    {
        try { await l.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs }); return true; }
        catch (TimeoutException) { return false; }
    }

    /// <summary>
    /// Diagnostic (read-only): opens a match, picks a dropdown option and optionally a filter tab (e.g. "Intervals"),
    /// saves a screenshot and returns the visible text of the markets area, to learn block titles.
    /// </summary>
    public async Task<string> DescribeMatchPageAsync(string matchUrl, string option, string? tab)
    {
        var page = await Page();
        await OpenMatchAsync(page, matchUrl.StartsWith("http") ? matchUrl : At(matchUrl));
        await SelectSectionAsync(page, new MarketSection { Option = option, Title = "" });
        if (!string.IsNullOrEmpty(tab))
        {
            var t = page.Locator(o.FilterTab).Filter(new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(tab)}\s*$", RegexOptions.IgnoreCase) }).First;
            if (await IsVisible(t, 5_000)) { await t.ClickAsync(); await page.WaitForTimeoutAsync(3_000); }
        }
        await SaveDebug(page, $"describe-{option}-{tab}".Replace(' ', '-').ToLowerInvariant());
        var grid = page.Locator(".market-grid--theme-gray-60, [class*='market-grid']").First;
        return await IsVisible(grid, 5_000) ? await grid.InnerTextAsync() : await page.Locator("body").InnerTextAsync();
    }

    private async Task OpenMatchAsync(IPage page, string url)
    {
        await page.OpenAsync(url, log);
        await page.Locator(o.PeriodDropdown).First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.Locator(o.MarketsLoader).First.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
    }

    /// <summary>
    /// Interval "nothing happens" rows, read from screenshots of the section's "Intervals" filter:
    /// "16-30 Mins - No" → 16-30 Under 0.5, and "Under 1.5 In 60 Minute" → 1-60 Under 1.5 (from kickoff).
    /// </summary>
    private async Task<List<(string interval, decimal line, decimal odds, string name)>> ReadIntervalRowsAsync(IPage page, MarketSection section)
    {
        if (!await SwitchSectionAsync(page, section)) return [];
        var tab = page.Locator(o.FilterTab).Filter(new() { HasTextRegex = new Regex(@"^\s*Intervals\s*$", RegexOptions.IgnoreCase) }).First;
        if (await IsVisible(tab, 3_000)) { await tab.ClickAsync(); await page.WaitForTimeoutAsync(2_000); }
        var shots = await CaptureGridAsync(page);
        var rows = await reader.ReadIntervalsAsync(shots, CancellationToken.None);
        log.LogInformation("Coldbet: read {Count} interval rows from {Shots} screenshot(s)", rows.Count, shots.Count);
        // Back to "All markets" so a following whole-match read sees its Total block.
        var all = page.Locator(o.FilterTab).Filter(new() { HasTextRegex = new Regex(@"^\s*All markets\s*$", RegexOptions.IgnoreCase) }).First;
        if (await IsVisible(all, 2_000)) { await all.ClickAsync(); await page.WaitForTimeoutAsync(1_500); }
        return rows.Where(r => r.Interval is not null && r.Side == "Under")
            .Select(r => (r.Interval!, r.Line, r.Odds, $"{r.Interval} Under {r.Line.ToString(CultureInfo.InvariantCulture)}")).ToList();
    }

    /// <summary>Chooses the section in the dropdown and confirms the dropdown now shows it.</summary>
    private async Task<bool> SwitchSectionAsync(IPage page, MarketSection section)
    {
        await SelectSectionAsync(page, new MarketSection { Market = section.Market, Option = section.Option, Title = "" });
        var now = (await page.Locator(o.PeriodDropdown).First.InnerTextAsync()).Trim();
        if (now.Equals(section.Option, StringComparison.OrdinalIgnoreCase)) return true;
        log.LogInformation("Coldbet: '{Option}' not available (dropdown shows '{Now}') on {Url}", section.Option, now, page.Url);
        return false;
    }

    /// <summary>
    /// Screenshots of the market grid, scrolling inside it (it scrolls on its own, like a window) so long blocks are covered.
    /// Scrolls back to the top afterwards.
    /// </summary>
    private async Task<List<byte[]>> CaptureGridAsync(IPage page)
    {
        await DismissNotificationsPopupAsync(page);
        var grid = page.Locator(o.MarketGrid).First;
        await grid.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var shots = new List<byte[]> { await grid.ScreenshotAsync() };
        var box = await grid.BoundingBoxAsync();
        if (box is null) return shots;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + Math.Min(box.Height / 2, 400));
        int scrolled = 0;
        for (int i = 1; i < o.MaxGridScreenshots; i++)
        {
            await page.Mouse.WheelAsync(0, o.GridScrollStep);
            scrolled += o.GridScrollStep;
            await page.WaitForTimeoutAsync(800);
            var next = await grid.ScreenshotAsync();
            if (next.AsSpan().SequenceEqual(shots[^1])) break; // reached the bottom
            shots.Add(next);
        }
        if (scrolled > 0) await page.Mouse.WheelAsync(0, -scrolled);
        return shots;
    }

    /// <summary>Bottom (page pixels) of the bars fixed to the top of the window (site header, menu), which cover the grid when scrolled.</summary>
    private const string StickyTopBarBottomJs = """
        () => {
            let bottom = 0;
            for (const el of document.querySelectorAll('body *')) {
                const pos = getComputedStyle(el).position;
                if (pos !== 'fixed' && pos !== 'sticky') continue;
                const r = el.getBoundingClientRect();
                if (r.top <= 2 && r.bottom > 0 && r.width > innerWidth / 2 && r.height < innerHeight / 3) bottom = Math.max(bottom, r.bottom);
            }
            return bottom;
        }
        """;

    /// <summary>
    /// Scrolls the grid's own scroll areas back to the top, then brings the grid's top edge just below the sticky bar
    /// (scroll-margin works for the window and any scrolling container around the grid). Returns how many inner areas were reset.
    /// </summary>
    private const string GridToTopJs = """
        (grid, bar) => {
            const inner = [grid, ...grid.querySelectorAll('*')]
                .filter(e => e.scrollHeight > e.clientHeight + 5 && /(auto|scroll)/.test(getComputedStyle(e).overflowY));
            inner.forEach(e => e.scrollTop = 0);
            grid.style.scrollMarginTop = (bar + 10) + 'px';
            grid.scrollIntoView({ block: 'start', behavior: 'instant' });
            return inner.length;
        }
        """;

    /// <summary>Closes Coldbet's "Show notifications" popup with BLOCK (no notifications), if it is showing.</summary>
    private async Task DismissNotificationsPopupAsync(IPage page)
    {
        // Best effort: a pop-up that will not close must not stop the run (a click it hides fails on its own later).
        try
        {
            var block = page.Locator(o.NotificationsBlock).Locator("visible=true").First;
            if (await IsVisible(block, 1_000))
            {
                await block.ClickAsync(new() { Timeout = 5_000 });
                await page.WaitForTimeoutAsync(500);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            log.LogWarning("Coldbet: could not close the notifications pop-up: {Error}", ex.Message.Split('\n')[0]);
        }
    }

    private async Task<ILocator?> SelectSectionAsync(IPage page, MarketSection section)
    {
        await DismissNotificationsPopupAsync(page);
        var current = page.Locator(o.PeriodDropdown).First;
        if (!(await current.InnerTextAsync()).Trim().Equals(section.Option, StringComparison.OrdinalIgnoreCase))
        {
            // Open the "Regular time ▾" dropdown (click its box, not just the text) and wait for the list.
            var option = page.Locator(o.PeriodOption).Filter(new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(section.Option)}\s*$", RegexOptions.IgnoreCase) }).First;
            for (int attempt = 1; attempt <= 2 && !await IsVisible(option, 500); attempt++)
            {
                await page.Locator(o.PeriodDropdownBox).First.ClickAsync(new() { Timeout = 10_000 });
                if (await IsVisible(option, 5_000)) break;
            }
            if (!await IsVisible(option, 500))
            {
                await page.Keyboard.PressAsync("Escape");
                log.LogInformation("Coldbet: '{Option}' not in the dropdown on {Url}", section.Option, page.Url);
                return null; // this match has no such section
            }
            await option.ClickAsync(new() { Timeout = 10_000 });
            await page.Locator(o.MarketsLoader).First.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(1_000);
            // Make sure the switch really happened before reading anything.
            var now = (await current.InnerTextAsync()).Trim();
            if (!now.Equals(section.Option, StringComparison.OrdinalIgnoreCase))
            {
                log.LogWarning("Coldbet: dropdown shows '{Now}' after choosing '{Option}' on {Url}", now, section.Option, page.Url);
                await SaveDebug(page, $"dropdown-not-switched-{section.Market}", fullPage: false);
                return null;
            }
        }
        if (string.IsNullOrEmpty(section.Title)) return null; // only switching the dropdown
        // Whole title only, any kind of space between words ("Total. Corners"), so "Total 1. Corners" doesn't match.
        var pattern = "^\\s*" + string.Join(@"[\s ]+", section.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + "\\s*$";
        // Visible ones only: the site keeps hidden copies of market blocks in the page.
        var title = page.Locator($"text=/{pattern}/i >> visible=true").First;
        if (await IsVisible(title, 10_000)) return title;

        var seen = await page.EvaluateAsync<string[]>(@"() => [...document.querySelectorAll('body *')]
            .filter(e => e.children.length <= 1 && e.offsetParent !== null && /Total/.test(e.textContent) && e.textContent.length < 40)
            .slice(0, 12).map(e => JSON.stringify(e.textContent))");
        log.LogInformation("Coldbet: visible 'Total' texts: {Seen}", string.Join(", ", seen));
        return null;
    }

    internal static decimal? ParseNumber(string text)
    {
        var m = Regex.Match(text.Replace(" ", "").Replace(" ", ""), @"\d+(?:[.,]\d+)*");
        if (!m.Success) return null;
        var s = m.Value;
        // "12,345.67" / "12345,67" / "12,345" → invariant decimal
        if (s.Contains(',') && s.Contains('.')) s = s.Replace(",", "");
        else if (s.Count(c => c == ',') == 1 && s.Split(',')[1].Length <= 2) s = s.Replace(',', '.');
        else s = s.Replace(",", "");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Diagnostic: the logged-in session (cookies) as JSON, to open the same login in another browser context.</summary>
    public Task<string> ExportSessionAsync() => browserTab.Page.Context.StorageStateAsync();

    /// <summary>Diagnostic: the Playwright instance, to open a phone-sized context with the same session.</summary>
    public IBrowser Browser => browserTab.Page.Context.Browser!;

    /// <summary>Logs out through the My account menu and checks the Log in button is back.</summary>
    public async Task LogoutAsync()
    {
        if (_page is null || !_loggedIn) return;
        var page = _page;
        try
        {
            var menu = page.Locator(o.AccountMenu).First;
            await menu.HoverAsync(new() { Timeout = 10_000 });
            var logout = page.Locator(o.LogoutButton).First;
            if (!await IsVisible(logout, 3_000))
            {
                await menu.ClickAsync(new() { Timeout = 10_000 }); // some menus open on click, or navigate to the office page
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            }
            await logout.ClickAsync(new() { Timeout = 15_000 });
            var confirm = page.Locator(o.LogoutConfirm).First;
            if (await IsVisible(confirm, 3_000)) await confirm.ClickAsync();
            await page.Locator(o.LoginButton).First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            _loggedIn = false;
            log.LogInformation("Logged out of {Site} ({User})", _origin.Host, account.Username);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            var file = await SaveDebug(page, "logout");
            log.LogWarning("Logout failed for {User}: {Error} (screenshot: {File}). The browser is closed anyway.",
                account.Username, ex.Message.Split('\n')[0], file);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Log out at the end of a run, success or failure, unless the bet slip was filled for the user to place.
        if (!browserTab.LeaveOpen) await LogoutAsync();
        await browserTab.DisposeAsync(); // the site's tab stays open for the next run (with the filled bet slip, if any)
    }
}
