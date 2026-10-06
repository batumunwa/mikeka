using Mikeka.Api.Bookmaker;

namespace Mikeka.Tests;

public class OneWinTests
{
    [Fact]
    public void ParseCard_reads_teams_and_kickoff_from_a_match_card()
    {
        // Text of a 1win match card as seen on the live site (EAT times).
        var text = "22:45\n•\n05/10/2026\nDeportivo Riestra\nCentral Cordoba Santiago del Estero\nFull time result\n1\n1.88\nx\n3.14\n2\n4.83\n+99";
        var (home, away, kickoff) = OneWinClient.ParseCard(text);
        Assert.Equal("Deportivo Riestra", home);
        Assert.Equal("Central Cordoba Santiago del Estero", away);
        Assert.Equal(new DateTime(2026, 10, 5, 19, 45, 0, DateTimeKind.Utc), kickoff); // 22:45 EAT = 19:45 UTC
    }

    [Theory]
    [InlineData("Under 9.5 1.49", "Under", 9.5, 1.49)]
    [InlineData("Under 0.5 1.14", "Under", 0.5, 1.14)]
    [InlineData("Over 7.5 1.45", "Over", 7.5, 1.45)]
    public void ParseTotalRow_reads_side_line_and_odds(string text, string side, double line, double odds)
    {
        var r = PageText.ParseTotalRow(text)!.Value;
        Assert.Equal(side, r.side);
        Assert.Equal((decimal)line, r.line);
        Assert.Equal((decimal)odds, r.odds);
    }

    [Theory]
    [InlineData("https://1wnorh.life", "1win")]
    [InlineData("https://1win.com/betting", "1win")]
    [InlineData("https://coldbet1f.com/en/line/football", "coldbet")]
    public void GuessSite_from_url(string url, string site) => Assert.Equal(site, SiteBookmakerFactory.GuessSite(url));
}
