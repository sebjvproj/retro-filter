using System.Text.Json;

namespace RetroFilter;

/// <summary>
/// Réglages mémorisés d'une session à l'autre, dans %APPDATA%\retro-filter\settings.json.
/// Les choix sont enregistrés par nom (« ega », « moderne »...) et non par numéro,
/// pour survivre à l'ajout de cartes, modes ou palettes.
/// </summary>
sealed class AppSettings
{
    public string Machine { get; set; } = "cga";
    public string Mode { get; set; } = "moderne";

    /// <summary>Dernière palette choisie pour chaque carte.</summary>
    public Dictionary<string, string> Palettes { get; set; } = new();

    public bool InfoBar { get; set; }

    /// <summary>Filtrer seulement une zone tracée à la souris, et non toute la fenêtre.</summary>
    public bool ZoneOnly { get; set; }

    /// <summary>Raccourcis Ctrl+Alt+C, T, P, I actifs pendant le filtre (désactivés par défaut).</summary>
    public bool ExtraHotkeys { get; set; }

    /// <summary>L'explication « l'app continue dans la zone de notification » a déjà été montrée.</summary>
    public bool TrayHintShown { get; set; }

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "retro-filter", "settings.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Réglages non enregistrés : l'app continue avec ceux en mémoire.
        }
    }
}
