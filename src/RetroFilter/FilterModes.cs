namespace RetroFilter;

/// <summary>
/// Une palette proposée : nom parlant (ses couleurs), nom historique, nom de fichier,
/// index de palette du shader (voir DitherTable) et correspondance par luminosité.
/// </summary>
sealed record PaletteChoice(string Name, string Detail, string File, int Palette, bool MatchLuminance);

/// <summary>Une carte graphique émulée, sa description courte et ses palettes.</summary>
sealed record Machine(string Name, string Detail, string File, PaletteChoice[] Palettes);

/// <summary>Cartes, modes et palettes proposés à l'utilisateur, dans l'ordre où on les fait défiler.</summary>
static class FilterModes
{
    public static readonly Machine[] Machines =
    [
        new("CGA", L.T("4 couleurs (1981)", "4 colors (1981)"), "cga",
        [
            new(L.T("cyan, magenta, blanc", "cyan, magenta, white"), L.T("palette 1 haute intensité", "palette 1, high intensity"), "pal1-haute", 0, false),
            new(L.T("cyan, magenta, gris (sombre)", "cyan, magenta, gray (dark)"), L.T("palette 1 basse intensité", "palette 1, low intensity"), "pal1-basse", 1, false),
            new(L.T("vert, rouge, jaune", "green, red, yellow"), L.T("palette 0 haute intensité", "palette 0, high intensity"), "pal0-haute", 2, false),
            new(L.T("vert, rouge, marron (sombre)", "green, red, brown (dark)"), L.T("palette 0 basse intensité", "palette 0, low intensity"), "pal0-basse", 3, false),
            new(L.T("cyan, rouge, blanc", "cyan, red, white"), "mode 5", "mode5", 4, false),
            new(L.T("gris", "grayscale"), L.T("4 niveaux de gris", "4 shades of gray"), "gris", 5, true),
        ]),
        // En 320x200 et 640x200, l'EGA n'affiche que les 16 couleurs fixes du CGA.
        new("EGA", L.T("16 couleurs (1984)", "16 colors (1984)"), "ega",
        [
            new(L.T("16 couleurs", "16 colors"), L.T("les 16 couleurs fixes de l'EGA en 320x200", "the 16 fixed EGA colors at 320x200"), "16-couleurs", 5, false),
            new(L.T("gris", "grayscale"), L.T("4 niveaux de gris", "4 shades of gray"), "gris", 5, true),
        ]),
    ];

    /// <summary>Nom affiché, nom de fichier, taille des gros pixels (0 = 320x200 fixe).</summary>
    public static readonly (string Name, string File, int PixelSize)[] All =
    [
        (L.T("moderne", "modern"), "moderne", 1),
        ("pixel 2", "p2", 2),
        ("pixel 3", "p3", 3),
        ("pixel 4", "p4", 4),
        (L.T("historique 320x200", "period 320x200"), "historique", 0),
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

    public int PaletteIndex => _palettes[MachineIndex];

    public void NextMachine() => MachineIndex = (MachineIndex + 1) % FilterModes.Machines.Length;
    public void NextMode() => ModeIndex = (ModeIndex + 1) % FilterModes.All.Length;
    public void NextPalette() => _palettes[MachineIndex] = (_palettes[MachineIndex] + 1) % Machine.Palettes.Length;

    public void SetMachine(int index) => MachineIndex = index;
    public void SetMode(int index) => ModeIndex = index;
    public void SetPalette(int index) => _palettes[MachineIndex] = index;

    /// <summary>Reprend les choix mémorisés ; un nom inconnu garde la valeur par défaut.</summary>
    public void Load(AppSettings settings)
    {
        MachineIndex = Math.Max(0, Array.FindIndex(FilterModes.Machines, m => m.File == settings.Machine));
        ModeIndex = Math.Max(0, Array.FindIndex(FilterModes.All, m => m.File == settings.Mode));
        for (int i = 0; i < FilterModes.Machines.Length; i++)
        {
            var machine = FilterModes.Machines[i];
            if (settings.Palettes.TryGetValue(machine.File, out var file))
                _palettes[i] = Math.Max(0, Array.FindIndex(machine.Palettes, p => p.File == file));
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Machine = Machine.File;
        settings.Mode = Mode.File;
        for (int i = 0; i < FilterModes.Machines.Length; i++)
            settings.Palettes[FilterModes.Machines[i].File] = FilterModes.Machines[i].Palettes[_palettes[i]].File;
    }

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
