using UpDownLoaderBot.Bot;

namespace UpDownLoaderBot.Tests;

public class LocalizedTextTests
{
    private static readonly LocalizedText Text = new(
        belarusian: "be", english: "en", polish: "pl", russian: "ru", ukrainian: "uk");

    [Theory]
    [InlineData("be", "be")]
    [InlineData("en", "en")]
    [InlineData("pl", "pl")]
    [InlineData("ru", "ru")]
    [InlineData("uk", "uk")]
    public void Answers_in_the_language_of_the_client(string languageCode, string expected)
    {
        Assert.Equal(expected, Text.For(languageCode));
    }

    // Telegram sends an IETF tag, so the region rides along and the case is not guaranteed.
    [Theory]
    [InlineData("uk-UA", "uk")]
    [InlineData("pt-BR", "en")]
    [InlineData("RU", "ru")]
    [InlineData("BE-by", "be")]
    public void Reads_the_language_out_of_a_full_tag(string languageCode, string expected)
    {
        Assert.Equal(expected, Text.For(languageCode));
    }

    // A language nobody translated it into, an empty tag, and a channel post — no sender to ask.
    [Theory]
    [InlineData("de")]
    [InlineData("")]
    [InlineData(null)]
    public void Falls_back_to_english(string? languageCode)
    {
        Assert.Equal("en", Text.For(languageCode));
    }
}

public class BotTextsTests
{
    [Theory]
    [InlineData("be", "Прывітанне!")]
    [InlineData("en", "Hi!")]
    [InlineData("pl", "Cześć!")]
    [InlineData("ru", "Привет!")]
    [InlineData("uk", "Привіт!")]
    public void Greets_in_every_language_it_claims_to_speak(string languageCode, string greeting)
    {
        Assert.StartsWith(greeting, BotTexts.StartInstructions.For(languageCode));
    }

    // Catches a translation that lost something on the way, which reading them side by side does not.
    [Theory]
    [InlineData("be")]
    [InlineData("en")]
    [InlineData("pl")]
    [InlineData("ru")]
    [InlineData("uk")]
    public void Names_the_link_shapes_it_accepts_in_every_language(string languageCode)
    {
        var text = BotTexts.StartInstructions.For(languageCode);

        Assert.Contains("instagram.com/reel/", text);
        Assert.Contains("/p/", text);
        Assert.Contains("/tv/", text);
        Assert.Contains("👎", text);
    }
}
