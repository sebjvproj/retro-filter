namespace RetroFilter;

/// <summary>Une palette proposée : index de palette du shader (voir DitherTable) et correspondance par luminosité.</summary>
sealed record PaletteChoice(string Name, string File, int Palette, bool MatchLuminance);

/// <summary>Une carte graphique émulée et ses palettes.</summary>
sealed record Machine(string Name, string File, PaletteChoice[] Palettes);

/// <summary>Cartes, modes et palettes proposés à l'utilisateur, dans l'ordre où on les fait défiler.</summary>
static class FilterModes
{
    public static readonly Machine[] Machines =
    [
        new("CGA", "cga",
        [
            new("palette 1 haute intensité", "pal1-haute", 0, false),
            new("palette 1 basse intensité", "pal1-basse", 1, false),
            new("palette 0 haute intensité", "pal0-haute", 2, false),
            new("palette 0 basse intensité", "pal0-basse", 3, false),
            new("mode 5 (cyan, rouge, blanc)", "mode5", 4, false),
            new("gris", "gris", 5, true),
        ]),
        // En 320x200 et 640x200, l'EGA n'affiche que les 16 couleurs fixes du CGA.
        new("EGA", "ega",
        [
            new("16 couleurs", "16-couleurs", 5, false),
            new("gris", "gris", 5, true),
        ]),
    ];

    /// <summary>Nom affiché, nom de fichier, taille des gros pixels (0 = 320x200 fixe).</summary>
    public static readonly (string Name, string File, int PixelSize)[] All =
    [
        ("moderne", "moderne", 1),
        ("pixel 2", "p2", 2),
        ("pixel 3", "p3", 3),
        ("pixel 4", "p4", 4),
        ("historique 320x200", "historique", 0),
    ];

    /// <summary>Toutes les palettes de toutes les cartes, à plat (pour les modes de test).</summary>
    public static readonly (Machine Machine, PaletteChoice Palette)[] AllPalettes =
        Machines.SelectMany(m => m.Palettes.Select(p => (m, p))).ToArray();

    public static void ApplyPalette(CgaSettings settings, PaletteChoice palette)
    {
        settings.Palette = palette.Palette;
        settings.MatchLuminance = palette.MatchLuminance;
    }

    /// <summary>Dossier des images enregistrées.</summary>
    public static readonly string SnapshotDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "retro-filter");
}

/// <summary>
/// Choix courant de l'utilisateur : carte, mode et palette (une palette retenue par carte).
/// Partagé entre la fenêtre principale et l'overlay, pour être conservé d'un jeu à l'autre.
/// </summary>
sealed class FilterState
{
    readonly int[] _palettes = new int[FilterModes.Machines.Length];

    public int MachineIndex { get; private set; }
    public int ModeIndex { get; private set; }

    public Machine Machine => FilterModes.Machines[MachineIndex];
    public (string Name, string File, int PixelSize) Mode => FilterModes.All[ModeIndex];
    public PaletteChoice Palette => Machine.Palettes[_palettes[MachineIndex]];

    public void NextMachine() => MachineIndex = (MachineIndex + 1) % FilterModes.Machines.Length;
    public void NextMode() => ModeIndex = (ModeIndex + 1) % FilterModes.All.Length;
    public void NextPalette() => _palettes[MachineIndex] = (_palettes[MachineIndex] + 1) % Machine.Palettes.Length;

    /// <summary>Par exemple « EGA moderne, 16 couleurs ».</summary>
    public string Description => $"{Machine.Name} {Mode.Name}, {Palette.Name}";

    /// <summary>Par exemple « ega-moderne_16-couleurs », pour les noms de fichiers.</summary>
    public string FileLabel => $"{Machine.File}-{Mode.File}_{Palette.File}";

    public void Apply(CgaSettings settings)
    {
        settings.PixelSize = Mode.PixelSize;
        FilterModes.ApplyPalette(settings, Palette);
    }
}
