using System.Globalization;

namespace RetroFilter;

/// <summary>
/// Langue de l'interface : français ou anglais. Chaque texte est écrit dans les deux langues
/// à l'endroit où il sert : L.T("Quitter", "Quit"). La langue est fixée une fois, au démarrage.
/// </summary>
static class L
{
    public static bool English { get; private set; }

    /// <summary>« fr » ou « en ».</summary>
    public static string Code => English ? "en" : "fr";

    /// <param name="code">« fr », « en », ou null pour suivre la langue de Windows (français si Windows est en français, anglais sinon).</param>
    public static void Init(string? code)
    {
        English = code switch
        {
            "fr" => false,
            "en" => true,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "fr",
        };
    }

    public static string T(string french, string english) => English ? english : french;
}
