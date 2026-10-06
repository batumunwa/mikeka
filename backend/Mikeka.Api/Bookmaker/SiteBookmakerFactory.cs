using Mikeka.Api.Domain;

namespace Mikeka.Api.Bookmaker;

/// <summary>Chooses the site client for each account from its Site ("coldbet" or "1win").</summary>
public class SiteBookmakerFactory(ColdbetFactory coldbet, OneWinFactory oneWin, LeonbetFactory leonbet) : IBookmakerFactory
{
    public static readonly string[] Sites = ["coldbet", "1win", "leonbet"];

    public Task<IBookmakerClient> CreateAsync(Account account, string password, CancellationToken ct) =>
        account.Site.ToLowerInvariant() switch
        {
            "1win" => oneWin.CreateAsync(account, password, ct),
            "leonbet" => leonbet.CreateAsync(account, password, ct),
            "coldbet" => coldbet.CreateAsync(account, password, ct),
            _ => throw new InvalidOperationException($"Unknown site '{account.Site}' on account {account.Name}."),
        };

    /// <summary>Best guess from the URL, used when an account is saved without a site.</summary>
    public static string GuessSite(string url) =>
        !Uri.TryCreate(url, UriKind.Absolute, out var u) ? "coldbet"
        : u.Host.StartsWith("1w") || u.Host.Contains("1win") ? "1win"
        : u.Host.Contains("leonbet") ? "leonbet"
        : "coldbet";
}
