namespace UpDownLoaderBot.Bot;

/// <summary>
///     One user-facing message in every language the bot speaks, English standing in for the rest.
///     The texts themselves live in <see cref="BotTexts" />; ask for one through <see cref="For" />,
///     which is why the languages are not exposed one by one — a caller reaching for a single
///     language would skip the fallback.
/// </summary>
public sealed class LocalizedText
{
    public LocalizedText(string belarusian, string english, string polish, string russian, string ukrainian)
    {
        Belarusian = belarusian;
        English = english;
        Polish = polish;
        Russian = russian;
        Ukrainian = ukrainian;
    }

    private string Belarusian { get; }

    private string English { get; }

    private string Polish { get; }

    private string Russian { get; }

    private string Ukrainian { get; }

    /// <param name="languageCode">
    ///     What Telegram puts in <c>from.language_code</c>: an IETF tag, so it may carry a region
    ///     (<c>en-GB</c>, <c>pt-BR</c>), and it is absent altogether on a channel post.
    /// </param>
    public string For(string? languageCode) =>
        PrimaryLanguage(languageCode) switch
        {
            "be" => Belarusian,
            "pl" => Polish,
            "ru" => Russian,
            "uk" => Ukrainian,
            _ => English
        };

    // The region says nothing about the wording, and an IETF tag is case-insensitive — neither is
    // something to compare against.
    private static string PrimaryLanguage(string? languageCode) =>
        languageCode?.Split('-')[0].ToLowerInvariant() ?? "";
}
