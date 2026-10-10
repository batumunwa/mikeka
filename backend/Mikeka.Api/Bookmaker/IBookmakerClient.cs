using Mikeka.Api.Domain;
using Mikeka.Api.Services;

namespace Mikeka.Api.Bookmaker;

public enum BetOutcome { Pending, Won, Lost }

/// <summary>Everything the engine needs from a betting site. One instance per account session.</summary>
public interface IBookmakerClient : IAsyncDisposable
{
    Task LoginAsync(CancellationToken ct);
    Task<decimal> GetBalanceAsync(CancellationToken ct);
    /// <summary>Football matches in <paramref name="leagues"/> (reading only <paramref name="markets"/>) kicking off before <paramref name="untilUtc"/>, with their cards/corners totals.</summary>
    Task<IReadOnlyList<MatchInfo>> GetMatchesAsync(IReadOnlyCollection<string> leagues, IReadOnlyList<MarketChoice> markets, DateTime untilUtc, CancellationToken ct);
    /// <summary>Places one accumulator and returns the bookmaker's bet reference.</summary>
    Task<string> PlaceSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct);
    /// <summary>
    /// Clicks each pick's odds so the site's bet slip holds them, and stops: "Place" is never clicked. The tab is left open
    /// and logged in for the user to place the bet. Throws (bet slip emptied) if a pick can't be added.
    /// Returns true when the stake was typed into the site's stake box too (false: the user types it).
    /// </summary>
    Task<bool> FillSlipAsync(IReadOnlyList<Pick> picks, decimal stake, CancellationToken ct);
    /// <summary>The slip's result on the site (logged in). Pending also when the bet is not found.</summary>
    Task<BetOutcome> GetOutcomeAsync(Slip slip, CancellationToken ct);
}

/// <summary>
/// "Place bet" was clicked but the site did not confirm with a bet number: the bet may or may not exist.
/// Any other exception from <see cref="IBookmakerClient.PlaceSlipAsync"/> means nothing was placed.
/// </summary>
public class BetUnconfirmedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A pick of the chosen slip can no longer be taken (odds moved outside the range, market gone): choose a new slip.</summary>
public class PickGoneException(string message) : InvalidOperationException(message);

public interface IBookmakerFactory
{
    Task<IBookmakerClient> CreateAsync(Account account, string password, CancellationToken ct);
}
