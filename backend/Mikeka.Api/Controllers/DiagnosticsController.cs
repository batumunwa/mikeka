using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Mikeka.Api.Bookmaker;

namespace Mikeka.Api.Controllers;

/// <summary>
/// Read-only diagnostics for learning Coldbet's page layout. No login, no betting.
/// </summary>
[ApiController, Route("api/diagnostics")]
public class DiagnosticsController(IOptions<ColdbetOptions> options, IOptions<BrowserOptions> browserOptions) : ControllerBase
{
    /// <summary>
    /// Read-only: logs a Coldbet account in (desktop), opens <paramref name="url"/> with the same session in a phone-sized
    /// browser, waits, and reports whether markets show as text (and how many canvases there are). Then logs out.
    /// </summary>
    [HttpPost("coldbet-mobile-logged-in")]
    public async Task<IActionResult> ColdbetMobileLoggedIn(int accountId, string url, int wait = 20000,
        [FromServices] Data.MikekaDb db = null!, [FromServices] ColdbetFactory coldbet = null!,
        [FromServices] Services.AccountSecrets secrets = null!, CancellationToken ct = default)
    {
        var account = await db.Accounts.FindAsync([accountId], ct);
        if (account is null) return NotFound();
        await using var client = (ColdbetClient)await coldbet.CreateAsync(account, secrets.Unprotect(account.PasswordProtected), ct);
        await client.LoginAsync(ct);
        var session = await client.ExportSessionAsync();

        using var pw = await Playwright.CreateAsync();
        var phone = await client.Browser.NewContextAsync(new(pw.Devices["iPhone 13"])
        {
            Locale = "en-US", TimezoneId = "Africa/Dar_es_Salaam", StorageState = session,
        });
        var page = await phone.NewPageAsync();
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(wait);
        var o = options.Value;
        Directory.CreateDirectory(o.DebugDir);
        var stem = Path.Combine(o.DebugDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-mobile-logged-in");
        await page.ScreenshotAsync(new() { Path = stem + ".png", FullPage = true });
        await System.IO.File.WriteAllTextAsync(stem + ".html", await page.ContentAsync());
        var canvases = await page.Locator("canvas").CountAsync();
        var text = await page.Locator("body").InnerTextAsync();
        await phone.CloseAsync();
        // Disposing the client logs out of Coldbet.
        return Ok(new { finalUrl = page.Url, canvases, screenshot = stem + ".png", text = text.Length > 8000 ? text[..8000] : text });
    }

    /// <summary>
    /// Opens <paramref name="url"/> as a phone (mobile site), optionally clicks a visible text, saves a screenshot,
    /// and returns where it ended up and the page's visible text, to see whether markets are real text there.
    /// </summary>
    [HttpPost("mobile")]
    public async Task<IActionResult> Mobile(string url, string? click = null, int wait = 6000, bool desktop = false, string? linkFilter = null)
    {
        var o = options.Value;
        using var pw = await Playwright.CreateAsync();
        await using var browser = await BrowserLauncher.LaunchAsync(pw, browserOptions.Value);
        var context = desktop
            ? await browser.NewContextAsync(new() { Locale = "en-US", TimezoneId = "Africa/Dar_es_Salaam", ViewportSize = new() { Width = 1366, Height = 900 } })
            : await browser.NewContextAsync(new(pw.Devices["iPhone 13"]) { Locale = "en-US", TimezoneId = "Africa/Dar_es_Salaam" });
        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(wait);
        // Several clicks in a row, separated by '>' (e.g. "Argentina>Primera Division"); a step starting with '~' matches part of the text.
        var clicked = new List<string>();
        foreach (var step in (click ?? "").Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var target = step.StartsWith('~')
                ? page.GetByText(step[1..]).First
                : page.GetByText(step, new() { Exact = true }).First;
            try { await target.ClickAsync(new() { Timeout = 10_000 }); await page.WaitForTimeoutAsync(Math.Min(wait, 6000)); clicked.Add(step + ": ok"); }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { clicked.Add(step + ": not found"); break; }
        }
        Directory.CreateDirectory(o.DebugDir);
        var stem = Path.Combine(o.DebugDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{(desktop ? "desktop" : "mobile")}");
        await page.ScreenshotAsync(new() { Path = stem + ".png", FullPage = true });
        await System.IO.File.WriteAllTextAsync(stem + ".html", await page.ContentAsync());
        var canvases = await page.Locator("canvas").CountAsync();
        var text = await page.Locator("body").InnerTextAsync();
        var links = await page.EvaluateAsync<string[]>("f => [...new Set([...document.querySelectorAll('a[href]')].filter(a => !f || new RegExp(f, 'i').test(a.getAttribute('href'))).map(a => a.getAttribute('href') + ' | ' + a.innerText.replace(/\\s+/g, ' ').trim()))].slice(0, 200)", linkFilter ?? "");
        await context.CloseAsync();
        return Ok(new { finalUrl = page.Url, clicked, canvases, screenshot = stem + ".png", links, text = text.Length > 8000 ? text[..8000] : text });
    }
}
