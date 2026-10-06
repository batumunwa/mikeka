using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Mikeka.Api.Data;

namespace Mikeka.Api.Services;

/// <summary>Builds the Excel log: one row per slip with date, matches, odds, stake, result and balance.</summary>
public class ExcelLog(MikekaDb db, IConfiguration config)
{
    public string FilePath => config["ExcelLogPath"] ?? Path.Combine("logs", "slips.xlsx");

    public async Task<byte[]> BuildAsync(int? accountId, CancellationToken ct)
    {
        var slips = await db.Slips.AsNoTracking()
            .Include(s => s.Account).Include(s => s.Picks)
            .Where(s => accountId == null || s.AccountId == accountId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Slips");
        string[] headers = ["Date (EAT)", "Account", "Matches", "Picks", "Pick odds", "Combined odds", "Stake", "Result", "Potential return", "Balance before", "Balance after", "Bet ref", "Note"];
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        int r = 2;
        foreach (var s in slips)
        {
            var picks = s.Picks.OrderBy(p => p.Kickoff).ToList();
            ws.Cell(r, 1).Value = s.BetDay.ToString("yyyy-MM-dd");
            ws.Cell(r, 2).Value = s.Account?.Username;
            ws.Cell(r, 3).Value = string.Join("\n", picks.Select(p => $"{TimeZoneInfo.ConvertTimeFromUtc(p.Kickoff, Eat.Zone):dd/MM HH:mm} {p.Home} v {p.Away} ({p.League})"));
            ws.Cell(r, 4).Value = string.Join("\n", picks.Select(PickText));
            ws.Cell(r, 5).Value = string.Join("\n", picks.Select(p => p.Odds.ToString("0.00")));
            ws.Cell(r, 6).Value = s.CombinedOdds;
            ws.Cell(r, 7).Value = s.Stake;
            ws.Cell(r, 8).Value = s.Status.ToString();
            ws.Cell(r, 9).Value = s.PotentialReturn;
            if (s.BalanceBefore is { } bb) ws.Cell(r, 10).Value = bb;
            if (s.BalanceAfter is { } ba) ws.Cell(r, 11).Value = ba;
            ws.Cell(r, 12).Value = s.BetReference;
            ws.Cell(r, 13).Value = s.Note;
            ws.Row(r).Style.Alignment.WrapText = true;
            ws.Cell(r, 8).Style.Font.FontColor = s.Status switch
            {
                Domain.SlipStatus.Won => XLColor.Green,
                Domain.SlipStatus.Lost => XLColor.Red,
                _ => XLColor.Black,
            };
            r++;
        }
        ws.Columns(6, 11).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents(1, 200);
        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static readonly Dictionary<string, string> MarketNames = new()
    {
        ["goals"] = "goals", ["corners"] = "corners", ["cards"] = "yellow cards", ["fouls"] = "fouls",
    };

    /// <summary>"Total corners Under 9.5" or "No goals in minutes 16-30".</summary>
    public static string PickText(Domain.SlipPick p)
    {
        if (p.Label is not null) return p.Label;
        if (p.Market == "result") return $"Draw after minute {p.Interval?.Split('-')[^1]}";
        var name = MarketNames.GetValueOrDefault(p.Market, p.Market);
        return p.Interval is null ? $"Total {name} {p.Side} {p.Line}" : $"No {name} in minutes {p.Interval}";
    }

    /// <summary>Rewrites logs/slips.xlsx with all accounts. Called after every slip change.</summary>
    public async Task WriteFileAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        await File.WriteAllBytesAsync(FilePath, await BuildAsync(null, ct), ct);
    }
}
