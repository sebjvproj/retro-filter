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

    /// <summary>Dossier des images enregistrées.</summary>
    public static readonly string SnapshotDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "retro-filter");
}
