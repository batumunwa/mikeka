using Microsoft.Extensions.Options;
using Mikeka.Api.Bookmaker;
using Mikeka.Api.Data;

namespace Mikeka.Api.Services;

/// <summary>
/// Every 5 minutes: is the system Chrome (Browser:CdpUrl) answering? When it goes away, and when it is back, one Activity
/// line and one email (no repeats in between). A Chrome started fresh means 1win will likely ask for the puzzle at the next
/// login, so the user hears about it before a run waits for them.
/// </summary>
public class ChromeWatch(IOptions<BrowserOptions> browser, IServiceScopeFactory scopes, INotifier notifier, ILogger<ChromeWatch> log)
    : BackgroundService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var cdp = browser.Value.CdpUrl;
        if (string.IsNullOrWhiteSpace(cdp)) return; // the system launches its own window: nothing to watch
        bool? wasUp = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                var up = await IsUpAsync(cdp, ct);
                if (wasUp is not null && up != wasUp)
                {
                    var (subject, body) = up
                        ? ("Mikeka: Chrome is open again",
                           "The system Chrome answers again. If it was restarted, 1win will probably show the puzzle at the next login: solve it in that Chrome.")
                        : ("Mikeka: Chrome is closed",
                           "The system Chrome is not answering. The next run opens it by itself, but 1win will then probably ask for the puzzle. " +
                           "Best: open the Mikeka Chrome now and solve the puzzle when 1win shows it.");
                    await NoteAsync(up ? "Info" : "Warning", body);
                    await notifier.SendAsync(subject, body, ct);
                }
                else if (wasUp is null && !up) await NoteAsync("Warning", "The system Chrome is not answering (seen when the API started); the next run opens it.");
                wasUp = up;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Chrome check failed"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private static async Task<bool> IsUpAsync(string cdp, CancellationToken ct)
    {
        try { return (await Http.GetAsync(new Uri(new Uri(cdp), "/json/version"), ct)).IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return false; }
    }

    private async Task NoteAsync(string level, string message)
    {
        log.LogInformation("{Message}", message);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MikekaDb>();
        db.RunLogs.Add(new Domain.RunLog { Level = level, Message = message });
        await db.SaveChangesAsync();
    }
}
