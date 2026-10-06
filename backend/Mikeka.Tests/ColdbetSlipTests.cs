using System.Text.RegularExpressions;
using Mikeka.Api.Bookmaker;
using Mikeka.Api.Services;

namespace Mikeka.Tests;

public class ColdbetSlipTests
{
    private static Selection Sel(string market, decimal line, string? interval = null) => new(market, "Under", line, 1.15m, "", interval);

    [Theory]
    // Bet slip text seen on the live site on 2026-10-06: "Total: Under 3.5" for match goals.
    [InlineData("goals", 3.5, null, "Total: Under 3.5", true)]
    [InlineData("goals", 3.5, null, "Total: Over 3.5", false)]
    [InlineData("goals", 3.5, null, "Total: Under 3", false)]
    [InlineData("goals", 3, null, "Total: Under 3.5", false)]
    [InlineData("goals", 3.5, null, "Total 1: Under 3.5", false)]
    [InlineData("goals", 3.5, null, "Total. Corners: Under 3.5", false)]
    [InlineData("corners", 9.5, null, "Total. Corners: Under 9.5", true)]
    [InlineData("corners", 9.5, null, "Total. Yellow Cards: Under 9.5", false)]
    [InlineData("cards", 4.5, null, "Total. Yellow Cards: Under 4.5", true)]
    [InlineData("goals", 0.5, "16-30", "16-30 Mins - No", true)]
    [InlineData("goals", 0.5, "16-30", "16-30 Mins - Yes", false)]
    [InlineData("goals", 0.5, "16-30", "1-15 Mins - No", false)]
    [InlineData("goals", 0.5, "1-10", "Under 0.5 In 10 Minute", true)]
    [InlineData("goals", 1.5, "1-60", "Under 1.5 In 60 Minute", true)]
    [InlineData("goals", 1.5, "1-60", "Under 1.5 In 45 Minute", false)]
    public void Slip_name_must_be_exactly_the_pick(string market, double line, string? interval, string name, bool expected) =>
        Assert.Equal(expected, ColdbetClient.SlipNameMatches(Sel(market, (decimal)line, interval), name));

    [Theory]
    [InlineData("Bet No. 1234567890 accepted", "1234567890")]
    [InlineData("Your bet #98765432 has been placed", "98765432")]
    [InlineData("Coupon: 55512345", "55512345")]
    // Event codes in the bet slip ("280688. UEFA Nations League") and "My bets"/"Your bets" captions are not bet numbers.
    [InlineData("Bet slip1My bets280688. UEFA Nations League Kazakhstan - Faroe Islands", null)]
    [InlineData("YOUR BETS 280688. UEFA Nations League", null)]
    public void Bet_number_needs_a_bet_word_before_it(string text, string? expected)
    {
        var m = Regex.Match(text, new ColdbetOptions().BetIdRegex, RegexOptions.IgnoreCase);
        Assert.Equal(expected, m.Success ? m.Groups[1].Value : null);
    }

    [Fact]
    public void Place_button_text_must_say_place_a_bet()
    {
        var re = new Regex(new ColdbetOptions().PlaceButtonTextRegex, RegexOptions.IgnoreCase);
        Assert.Matches(re, "PLACE A BET");
        Assert.Matches(re, "Make a bet");
        Assert.DoesNotMatch(re, "REGISTRATION");
        Assert.DoesNotMatch(re, "Log in");
    }

    [Fact]
    public void Location_is_read_from_the_structured_answer()
    {
        var loc = ScreenshotOddsReader.ParseLocation("""{"found": true, "x": 512.5, "y": 240, "odds": 1.143, "label": "Under 3.5"}""");
        Assert.Equal(new CellLocation(512.5, 240, 1.143m, "Under 3.5"), loc);
        Assert.Null(ScreenshotOddsReader.ParseLocation("""{"found": false, "x": 0, "y": 0, "odds": 0, "label": ""}"""));
    }
}
