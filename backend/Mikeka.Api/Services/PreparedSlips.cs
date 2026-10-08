using System.Collections.Concurrent;
using System.Text.Json;

namespace Mikeka.Api.Services;

/// <summary>
/// The slip chosen from the public pages (before login), kept per account until it is placed. When the login or placing
/// fails, the next run continues with it instead of reading every league again (user's request 2026-10-08).
/// Kept in logs/prepared-slips.json, so an API restart does not lose it.
/// </summary>
public static class PreparedSlips
{
    /// <summary>Runs that may reuse one chosen slip; after that the matches are read again (a pick may have gone).</summary>
    public const int MaxReuses = 3;

    /// <summary>Minutes until the next try when a chosen slip could not be placed (login failed, puzzle not solved, …).</summary>
    public const int RetryMinutes = 5;

    public sealed record Prepared(SlipPlan Plan, decimal Stake, DateTime ChosenAtUtc, int Reuses);

    private static readonly string FilePath = Path.Combine("logs", "prepared-slips.json");
    private static readonly object FileLock = new();
    private static readonly ConcurrentDictionary<int, Prepared> Slips = Load();

    public static Prepared? Get(int accountId) => Slips.TryGetValue(accountId, out var p) ? p : null;

    public static void Save(int accountId, Prepared prepared)
    {
        Slips[accountId] = prepared;
        Write();
    }

    public static void Remove(int accountId)
    {
        if (Slips.TryRemove(accountId, out _)) Write();
    }

    private static void Write()
    {
        lock (FileLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Slips));
            }
            catch (IOException) { } // still kept in memory for this API run
        }
    }

    private static ConcurrentDictionary<int, Prepared> Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<int, Prepared>>(File.ReadAllText(FilePath)) is { } saved)
                return new(saved);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException) { }
        return new();
    }
}
