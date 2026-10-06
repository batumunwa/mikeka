using Mikeka.Api.Bookmaker;

namespace Mikeka.Tests;

public class ScreenshotReaderTests
{
    [Fact]
    public void Parse_reads_rows_from_the_structured_answer()
    {
        // Shape of the JSON Claude returns for the Coldbet "Total. Corners" screenshot read on 2026-10-05.
        var json = """
            {"found": true, "rows": [
              {"side": "Under", "line": 11, "odds": 1.16, "interval": null},
              {"side": "Under", "line": 11.5, "odds": 1.14, "interval": null},
              {"side": "Over", "line": 11.5, "odds": 3.89, "interval": null},
              {"side": "Under", "line": 0.5, "odds": 1.26, "interval": "1 - 15"}
            ]}
            """;
        var rows = ScreenshotOddsReader.Parse(json);
        Assert.Equal(4, rows.Count);
        Assert.Contains(new ReadRow("Under", 11.5m, 1.14m, null), rows);
        Assert.Equal("1-15", rows[3].Interval); // spaces removed so it matches the account's "1-15"
    }

    [Fact]
    public void Parse_returns_nothing_when_the_block_was_not_found()
    {
        Assert.Empty(ScreenshotOddsReader.Parse("""{"found": false, "rows": []}"""));
    }

    [Fact]
    public void Parse_ignores_values_that_are_not_prices()
    {
        var rows = ScreenshotOddsReader.Parse("""{"found": true, "rows": [{"side": "Under", "line": 9.5, "odds": 0.5, "interval": null}]}""");
        Assert.Empty(rows);
    }
}
