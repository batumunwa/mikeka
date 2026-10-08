using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Mikeka.Api.Data;
using Mikeka.Api.Domain;

namespace Mikeka.Api.Bookmaker;

/// <summary>
/// One Chrome window for every run, and ONE tab per betting site (so at most three tabs: Coldbet, Leonbet, 1win).
/// With Browser:CdpUrl set (the default), that window is your own Chrome, reached through its remote-debugging port:
/// the system never opens a second browser (and starts that Chrome once if it is not running).
/// Runs on the same site take turns in that site's tab; runs on different sites go side by side. When the tab is still
/// logged in as another account (e.g. its filled bet slip waits for you to place it), the next account waits until you
/// log out there, then logs in. A site's cookies are cleared before each login, so every run logs in fresh.
/// </summary>
public sealed class SharedBrowser(IOptions<BrowserOptions> options, IServiceScopeFactory scopes, ILogger<SharedBrowser> log) : IAsyncDisposable
{
    private static readonly TimeSpan MaxWaitForLogout = TimeSpan.FromHours(3);
    private static readonly string OwnersFile = Path.Combine("logs", "site-tabs.json");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _siteLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IPage> _tabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TabOwner> _owners = LoadOwners();
    private IPlaywright? _pw;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    private bool UseOwnChrome => !string.IsNullOrWhiteSpace(options.Value.CdpUrl);

    /// <summary>Which account last logged in on a site's tab, and whether its filled bet slip was left there for you.</summary>
    public sealed record TabOwner(int AccountId, string AccountName, bool SlipLeftOpen);

    /// <summary>
    /// The site's tab for <paramref name="account"/>'s run: waits while another run uses the site, then while the tab is
    /// still logged in as a different account (<paramref name="loginButton"/> shows once you have logged out).
    /// </summary>
    public async Task<BrowserTab> OpenTabAsync(Account account, string loginButton, CancellationToken ct)
    {
        var siteUrl = account.Url;
        var site = Regex.Replace(new Uri(siteUrl).Host, @"^www\.", "", RegexOptions.IgnoreCase);
        var siteLock = _siteLocks.GetOrAdd(site, _ => new SemaphoreSlim(1, 1));
        if (siteLock.CurrentCount == 0) log.LogInformation("Waiting for another run on {Site} to finish", site);
        await siteLock.WaitAsync(ct);
        try
        {
            var context = await ContextAsync(ct);
            var page = await SiteTabAsync(context, site, ct);
            if (_owners.TryGetValue(site, out var owner) && owner.AccountId != account.Id)
                page = await WaitForLogoutAsync(context, page, site, siteUrl, loginButton, owner, account, ct);

            await context.ClearCookiesAsync(new() { DomainRegex = new Regex(Regex.Escape(site) + "$", RegexOptions.IgnoreCase) });
            await page.BringToFrontAsync();
            SetOwner(site, new TabOwner(account.Id, account.Name, false));
            return new BrowserTab(page, siteLock,
                leftOpen => { if (leftOpen) SetOwner(site, new TabOwner(account.Id, account.Name, true)); },
                message => AddRunLogAsync(account.Id, "Info", message));
        }
        catch
        {
            siteLock.Release();
            throw;
        }
    }

    /// <summary>The site's one tab: the one used before, or one already open on the site (after an API restart), or a new one.</summary>
    private async Task<IPage> SiteTabAsync(IBrowserContext context, string site, CancellationToken ct)
    {
        if (_tabs.TryGetValue(site, out var known) && !known.IsClosed) return known;
        var page = context.Pages.FirstOrDefault(p => !p.IsClosed && IsOnSite(p, site)) ?? await context.NewPageAsync();
        _tabs[site] = page;
        return page;
    }

    private static bool IsOnSite(IPage page, string site) =>
        Uri.TryCreate(page.Url, UriKind.Absolute, out var u) && u.Host.EndsWith(site, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The tab is still logged in as <paramref name="owner"/>: wait (checking every 10 s, never reloading the page you work in)
    /// until the site's login button shows. Closing the tab also counts as done; the site is then reopened to check.
    /// </summary>
    private async Task<IPage> WaitForLogoutAsync(IBrowserContext context, IPage page, string site, string siteUrl, string loginButton,
        TabOwner owner, Account account, CancellationToken ct)
    {
        if (!IsOnSite(page, site)) await page.OpenAsync(siteUrl, log);
        if (await IsVisibleAsync(page.Locator(loginButton).First, 20_000)) return page;

        var what = owner.SlipLeftOpen ? "place the bet left in its slip (or not), then log out there" : "log out there";
        await AddRunLogAsync(account.Id, "Info",
            $"Waiting: the {site} tab is still logged in as {owner.AccountName}. {char.ToUpper(what[0])}{what[1..]}; {account.Name} logs in after that.");
        if (owner.AccountId != 0)
            await AddRunLogAsync(owner.AccountId, "Info", $"{account.Name} is waiting for the {site} tab: log out of {owner.AccountName} there when you are done.");

        var deadline = DateTime.UtcNow + MaxWaitForLogout;
        int loggedOutSeen = 0;
        while (loggedOutSeen < 2) // twice in a row, so a page that is still loading is not mistaken for "logged out"
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Waited {MaxWaitForLogout.TotalHours:0} h for {owner.AccountName} to be logged out of the {site} tab.");
            await Task.Delay(10_000, ct);
            if (page.IsClosed)
            {
                page = await context.NewPageAsync();
                _tabs[site] = page;
                await page.OpenAsync(siteUrl, log);
            }
            loggedOutSeen = await IsVisibleAsync(page.Locator(loginButton).First, 2_000) ? loggedOutSeen + 1 : 0;
        }
        await AddRunLogAsync(account.Id, "Info", $"{owner.AccountName} logged out of the {site} tab; {account.Name} continues.");
        return page;
    }

    private static async Task<bool> IsVisibleAsync(ILocator l, int timeoutMs)
    {
        try { await l.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs }); return true; }
        catch (TimeoutException) { return false; }
        catch (PlaywrightException) { return false; }
    }

    private async Task AddRunLogAsync(int accountId, string level, string message)
    {
        log.LogInformation("{Message}", message);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MikekaDb>();
            db.RunLogs.Add(new RunLog { AccountId = accountId, Level = level, Message = message });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { log.LogWarning(ex, "Could not save activity line"); }
    }

    // Who is logged in on each site's tab is kept in a small file, so an API restart still knows to wait.
    private void SetOwner(string site, TabOwner owner)
    {
        _owners[site] = owner;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OwnersFile)!);
            File.WriteAllText(OwnersFile, JsonSerializer.Serialize(_owners));
        }
        catch (Exception ex) { log.LogWarning(ex, "Could not save {File}", OwnersFile); }
    }

    private static ConcurrentDictionary<string, TabOwner> LoadOwners()
    {
        try
        {
            if (File.Exists(OwnersFile) && JsonSerializer.Deserialize<Dictionary<string, TabOwner>>(File.ReadAllText(OwnersFile)) is { } saved)
                return new(saved, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IBrowserContext> ContextAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_browser is null || !_browser.IsConnected || _context is null)
            {
                _pw ??= await Playwright.CreateAsync();
                if (UseOwnChrome) await ConnectToChromeAsync(ct);
                else await LaunchOwnWindowAsync();
            }
            return _context!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Connects to the Chrome listening on CdpUrl (starting it first if needed) and uses its window.</summary>
    private async Task ConnectToChromeAsync(CancellationToken ct)
    {
        var o = options.Value;
        IBrowser? browser = await TryConnectAsync(o);
        if (browser is null)
        {
            StartChrome(o);
            for (int i = 0; i < 30 && browser is null; i++)
            {
                await Task.Delay(1_000, ct);
                browser = await TryConnectAsync(o);
            }
            if (browser is null)
                throw new InvalidOperationException($"Chrome did not open its remote-debugging port at {o.CdpUrl}. Close all Chrome windows that use {o.ProfileDir} and try again.");
        }
        _browser = browser;
        // The window's own profile: new pages open as tabs in your Chrome window.
        _context = browser.Contexts.FirstOrDefault() ?? throw new InvalidOperationException("Connected to Chrome but it has no window.");
        _context.Close += (_, _) => _context = null;
        browser.Disconnected += (_, _) => { _browser = null; _context = null; };
        log.LogInformation("Using the Chrome window at {Url}: runs open tabs there", o.CdpUrl);
    }

    private async Task<IBrowser?> TryConnectAsync(BrowserOptions o)
    {
        try { return await _pw!.Chromium.ConnectOverCDPAsync(o.CdpUrl, new() { Timeout = 5_000, SlowMo = o.SlowMo }); }
        catch (PlaywrightException) { return null; }
        catch (TimeoutException) { return null; }
    }

    /// <summary>Starts Chrome as an ordinary program (not tied to the API): it stays open when runs end or the API restarts.</summary>
    private void StartChrome(BrowserOptions o)
    {
        var exe = !string.IsNullOrWhiteSpace(o.ChromePath) ? o.ChromePath : FindChrome()
            ?? throw new InvalidOperationException("Google Chrome not found. Set Browser:ChromePath in appsettings to chrome.exe.");
        var port = new Uri(o.CdpUrl).Port;
        Directory.CreateDirectory(o.ProfileDir);
        var args = $"--remote-debugging-port={port} --user-data-dir=\"{o.ProfileDir}\" --no-first-run --no-default-browser-check";
        // Started through WMI, Chrome is not the API's child: stopping the API (or the window/job it runs in) never closes
        // Chrome, so 1win's "verified human" state survives restarts (user's request 2026-10-08).
        if (!OperatingSystem.IsWindows() || !StartOutsideApi(exe, args))
            Process.Start(new ProcessStartInfo(exe) { Arguments = args, UseShellExecute = false });
        log.LogInformation("Started Chrome with remote debugging on port {Port} (profile {Dir})", port, o.ProfileDir);
    }

    private bool StartOutsideApi(string exe, string args)
    {
        try
        {
            var commandLine = $"\"{exe}\" {args}".Replace("'", "''");
            var script = $"$ProgressPreference='SilentlyContinue'; (Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{{CommandLine='{commandLine}'}}).ReturnValue";
            using var ps = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            })!;
            var output = ps.StandardOutput.ReadToEnd().Trim();
            ps.WaitForExit(30_000);
            if (output.Split('\n').Any(l => l.Trim() == "0")) return true;
            log.LogWarning("Starting Chrome through WMI returned {Code}; starting it directly", output);
        }
        catch (Exception ex) { log.LogWarning("Starting Chrome through WMI failed: {Error}; starting it directly", ex.Message); }
        return false;
    }

    private static string? FindChrome() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
    }.FirstOrDefault(File.Exists);

    /// <summary>Without CdpUrl: the system launches its own window and keeps it open with a blank tab between runs.</summary>
    private async Task LaunchOwnWindowAsync()
    {
        _browser = await BrowserLauncher.LaunchAsync(_pw!, options.Value);
        var context = await _browser.NewContextAsync(new()
        {
            Locale = "en-US",
            TimezoneId = "Africa/Dar_es_Salaam",
            ViewportSize = new() { Width = 1366, Height = 900 },
        });
        context.Close += (_, _) => _context = null;
        await context.NewPageAsync();
        _context = context;
    }

    public async ValueTask DisposeAsync()
    {
        // Your own Chrome stays open: only the connection is dropped. A window the system launched itself is closed.
        try { if (_browser is not null && !UseOwnChrome) await _browser.CloseAsync(); } catch (PlaywrightException) { }
        _pw?.Dispose();
    }
}

/// <summary>
/// One run's use of its site's tab. Disposing never closes the tab (it is the site's one tab, reused by the next run);
/// it lets the next run on the same site start.
/// </summary>
public sealed class BrowserTab(IPage page, SemaphoreSlim siteLock, Action<bool> onDone, Func<string, Task> note) : IAsyncDisposable
{
    private int _disposed;
    public IPage Page => page;
    /// <summary>Writes a line in this account's Activity (for things the user must do in the tab).</summary>
    public Task NoteAsync(string message) => note(message);
    /// <summary>Set when the bet slip was filled for the user: the run then stays logged in, and another account waits for your logout.</summary>
    public bool LeaveOpen { get; set; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
        try { onDone(LeaveOpen); }
        finally { siteLock.Release(); }
        return ValueTask.CompletedTask;
    }
}
