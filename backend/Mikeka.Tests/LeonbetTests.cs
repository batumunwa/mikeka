using Mikeka.Api.Bookmaker;

namespace Mikeka.Tests;

public class LeonbetTests
{
    private static readonly DateTime NowEat = new(2026, 10, 5, 20, 0, 0);

    [Fact]
    public void FindLeague_matches_country_and_league_name_ignoring_the_match_count()
    {
        string[][] links =
        [
            ["/bets/soccer/argentina/1970324836975749-copa-argentina", "Argentina - Cup 3"],
            ["/bets/soccer/argentina/1970324836975744-superliga", "Argentina - Super League 14"],
        ];
        Assert.Equal("/bets/soccer/argentina/1970324836975744-superliga", LeonbetClient.FindLeague(links, "Argentina. Super League"));
        Assert.Equal("/bets/soccer/argentina/1970324836975744-superliga", LeonbetClient.FindLeague(links, "argentina.super league"));
        Assert.Null(LeonbetClient.FindLeague(links, "Argentina. Primera Nacional"));
    }

    [Fact]
    public void FindLeague_accepts_the_league_name_alone_when_it_is_unique()
    {
        string[][] links =
        [
            ["/bets/soccer/europe/1970324836986742-uefa-nations-league", "Europe - UEFA Nations League 9"],
            ["/bets/soccer/argentina/1970324836975744-superliga", "Argentina - Super League 14"],
        ];
        Assert.Equal("/bets/soccer/europe/1970324836986742-uefa-nations-league", LeonbetClient.FindLeague(links, "UEFA Nations League"));
        Assert.Null(LeonbetClient.FindLeague(links, "U"));
    }

    [Theory]
    [InlineData("Today\n22:45\nDeportivo Riestra\nCentral Cordoba", "Deportivo Riestra", "Central Cordoba", 2026, 10, 5, 19, 45)]
    [InlineData("Tomorrow\n01:00\nVelez Sarsfield\nPlatense", "Velez Sarsfield", "Platense", 2026, 10, 5, 22, 0)]
    [InlineData("09.10\n20:30\nCA Aldosivi\nSarmiento De Junin", "CA Aldosivi", "Sarmiento De Junin", 2026, 10, 9, 17, 30)]
    public void ParseMatchLink_reads_teams_and_kickoff(string text, string home, string away, int y, int mo, int d, int h, int mi)
    {
        var (gotHome, gotAway, kickoff) = LeonbetClient.ParseMatchLink(text, NowEat);
        Assert.Equal(home, gotHome);
        Assert.Equal(away, gotAway);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc), kickoff); // EAT is UTC+3
    }

    [Fact]
    public void ParseMatchLink_skips_live_matches()
    {
        Assert.Null(LeonbetClient.ParseMatchLink("LIVE\n2nd half 61 minute\nCyprus\nLatvia", NowEat).home);
    }

    [Theory]
    [InlineData("Under (6.5) 1.21", "Under", 6.5, 1.21)]
    [InlineData("Under (3.5) 1.14", "Under", 3.5, 1.14)]
    [InlineData("Over (10.5) 3.35", "Over", 10.5, 3.35)]
    public void ParseRow_reads_side_line_and_odds(string text, string side, double line, double odds)
    {
        var r = LeonbetClient.ParseRow(text)!.Value;
        Assert.Equal((side, (decimal)line, (decimal)odds), r);
    }
}
