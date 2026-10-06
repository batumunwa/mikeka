using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Mikeka.Api.Bookmaker;

/// <summary>
/// "Browser" section in appsettings: which browser the system drives. "chrome" = the Google Chrome installed on this PC,
/// "msedge" = Microsoft Edge, "" = Playwright's own Chromium. A fresh, separate profile is always used — never the
/// person's everyday Chrome profile, bookmarks or saved logins.
/// </summary>
public class BrowserOptions
{
    public string Channel { get; set; } = "chrome";
    public bool Headless { get; set; } = false;
    public float SlowMo { get; set; } = 100;

    /// <summary>
    /// Work in your own Chrome window: the system connects to Chrome's remote-debugging port and only opens tabs in it.
    /// If that Chrome is not running, the system starts it once (it stays open after runs and API restarts).
    /// Empty = the system launches and owns its own window instead.
    /// </summary>
    public string CdpUrl { get; set; } = "http://localhost:9222";
    /// <summary>Profile folder of that Chrome. Chrome refuses remote debugging on your everyday profile, so it has its own.</summary>
    public string ProfileDir { get; set; } = @"C:\MikekaChrome";
    /// <summary>chrome.exe; empty = looked up in the usual install folders.</summary>
    public string ChromePath { get; set; } = "";
}

public static class BrowserLauncher
{
    public static Task<IBrowser> LaunchAsync(IPlaywright pw, BrowserOptions o) =>
        pw.Chromium.LaunchAsync(new()
        {
            Channel = string.IsNullOrWhiteSpace(o.Channel) ? null : o.Channel,
            Headless = o.Headless,
            SlowMo = o.Headless ? 0 : o.SlowMo,
        });
}
