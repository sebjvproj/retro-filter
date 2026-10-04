namespace RetroFilter;

/// <summary>Modes proposés à l'utilisateur, dans l'ordre où on les fait défiler.</summary>
static class FilterModes
{
    /// <summary>Nom affiché, nom de fichier, taille des gros pixels (0 = 320x200 fixe).</summary>
    public static readonly (string Name, string File, int PixelSize)[] All =
    [
        ("CGA moderne", "cga-moderne", 1),
        ("pixel 2", "cga-p2", 2),
        ("pixel 3", "cga-p3", 3),
        ("pixel 4", "cga-p4", 4),
        ("CGA historique 320x200", "cga-historique", 0),
    ];

    /// <summary>
    /// Palettes CGA : nom affiché, nom de fichier, index de palette du shader (0 à 5, voir
    /// Shaders/cga-v2.hlsl) et correspondance par luminosité (la palette 5 devient alors les 4 gris).
    /// </summary>
    public static readonly (string Name, string File, int Palette, bool MatchLuminance)[] Palettes =
    [
        ("palette 1 haute intensité", "pal1-haute", 0, false),
        ("palette 1 basse intensité", "pal1-basse", 1, false),
        ("palette 0 haute intensité", "pal0-haute", 2, false),
        ("palette 0 basse intensité", "pal0-basse", 3, false),
        ("mode 5 (cyan, rouge, blanc)", "mode5", 4, false),
        ("gris", "gris", 5, true),
        ("16 couleurs CGA", "16-couleurs", 5, false),
    ];

    /// <summary>Applique une palette de la liste aux réglages.</summary>
    public static void ApplyPalette(CgaSettings settings, int palette)
    {
        settings.Palette = Palettes[palette].Palette;
        settings.MatchLuminance = Palettes[palette].MatchLuminance;
    }

    /// <summary>Dossier des images enregistrées.</summary>
    public static readonly string SnapshotDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "retro-filter");
}
