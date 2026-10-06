using System.Globalization;
using System.Text.RegularExpressions;

namespace Mikeka.Api.Bookmaker;

/// <summary>Page scripts and parsing shared by the site clients for reading "Under 9.5 1.49"-style market rows.</summary>
public static class PageText
{
    /// <summary>Defines window.__mikekaCellText(el): the text of a whole outcome cell (name + odds) from its name element.</summary>
    public const string CellTextScript = @"() => { window.__mikekaCellText = c => {
        let cell = c;
        for (let j = 0; j < 4 && cell.parentElement && !/\d\s*$/.test(cell.innerText.replace(/^\s*(Over|Under)\s+[\d.,]+/, '')); j++)
            cell = cell.parentElement;
        return cell.innerText.replace(/\s+/g, ' ').trim();
    }; }";

    /// <summary>
    /// From a block's title element, climbs to the smallest ancestor holding "Over …"/"Under …" cells (the block itself)
    /// and returns each cell's text, e.g. "Under 9.5 1.49". Needs <see cref="CellTextScript"/> first.
    /// </summary>
    public const string TotalRowsScript = @"title => {
        const isRow = e => e.children.length === 0 && /^\s*(Over|Under)\s+\d/.test(e.textContent);
        let n = title;
        for (let i = 0; i < 10 && n.parentElement; i++) {
            n = n.parentElement;
            const names = [...n.querySelectorAll('*')].filter(isRow);
            if (names.length) return names.map(c => window.__mikekaCellText(c));
        }
        return [];
    }";

    /// <summary>Regex for an exact block title, allowing any kind of space between words.</summary>
    public static string TitlePattern(string title) =>
        "^\\s*" + string.Join(@"[\s ]+", title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + "\\s*$";

    /// <summary>"Under 9.5 1.49" → ("Under", 9.5, 1.49).</summary>
    public static (string side, decimal line, decimal odds)? ParseTotalRow(string text)
    {
        var m = Regex.Match(text, @"^\s*(Over|Under)\s+(\d+(?:[.,]\d+)?)\s+(\d+(?:[.,]\d+)?)\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var side = m.Groups[1].Value.Equals("under", StringComparison.OrdinalIgnoreCase) ? "Under" : "Over";
        return (side, decimal.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture),
                      decimal.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture));
    }
}
