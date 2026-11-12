using System.Runtime.InteropServices;

namespace RetroFilter;

/// <summary>Identité visuelle rétro : icône en pixel art, pastilles de palette, menus aux couleurs CGA.</summary>
static class RetroStyle
{
    public static readonly Color Black = Color.FromArgb(12, 12, 20);
    public static readonly Color Cyan = Color.FromArgb(85, 255, 255);
    public static readonly Color Magenta = Color.FromArgb(255, 85, 255);
    public static readonly Color White = Color.FromArgb(255, 255, 255);
    public static readonly Color Gray = Color.FromArgb(170, 170, 170);
    public static readonly Color DarkGray = Color.FromArgb(85, 85, 85);

    // Police à chasse fixe, pour rappeler les écrans de l'époque.
    public static readonly Font TextFont = new("Consolas", 10f);
    public static readonly Font BoldFont = new("Consolas", 10f, FontStyle.Bold);
    public static readonly Font BigFont = new("Consolas", 14f, FontStyle.Bold);
    public static readonly Font TitleFont = new("Consolas", 20f, FontStyle.Bold);

    // Moniteur 16x16 : écran tramé cyan et magenta (palette 1 haute intensité).
    static readonly string[] IconPixels =
    [
        "................",
        ".wwwwwwwwwwwwww.",
        ".wkkkkkkkkkkkkw.",
        ".wkcmcmcmcmcmkw.",
        ".wkmcmcmcmcmckw.",
        ".wkcmcmcmcmcmkw.",
        ".wkmcmcmcmcmckw.",
        ".wkcccccccccckw.",
        ".wkcccccccccckw.",
        ".wkmmmmmmmmmmkw.",
        ".wkkkkkkkkkkkkw.",
        ".wwwwwwwwwwwwww.",
        "......wwww......",
        ".....wwwwww.....",
        "...wwwwwwwwww...",
        "................",
    ];

    /// <summary>Icône de l'app (fenêtre et zone de notification), dessinée pixel par pixel.</summary>
    public static Icon CreateIcon(int size)
    {
        using var bitmap = new Bitmap(size, size);
        int scale = size / 16;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            Color? c = IconPixels[y][x] switch
            {
                'w' => White,
                'k' => Color.Black,
                'c' => Cyan,
                'm' => Magenta,
                _ => null,
            };
            if (c is not Color color) continue;
            for (int dy = 0; dy < scale; dy++)
            for (int dx = 0; dx < scale; dx++)
                bitmap.SetPixel(x * scale + dx, y * scale + dy, color);
        }
        IntPtr handle = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    /// <summary>Pastille 32x16 (fois l'échelle) montrant les couleurs d'une palette (4 cases, ou 8x2 pour 16 couleurs).</summary>
    public static Bitmap CreateSwatch(PaletteChoice palette, int scale = 1)
    {
        var settings = new CgaSettings();
        FilterModes.ApplyPalette(settings, palette);
        var colors = DitherTable.Colors(settings);
        var bitmap = new Bitmap(32 * scale, 16 * scale);
        using var g = Graphics.FromImage(bitmap);
        int columns = colors.Length <= 4 ? colors.Length : 8;
        int rows = (colors.Length + columns - 1) / columns;
        int w = 32 * scale / columns, h = 16 * scale / rows;
        for (int i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            using var brush = new SolidBrush(Color.FromArgb((int)MathF.Round(c.X * 255), (int)MathF.Round(c.Y * 255), (int)MathF.Round(c.Z * 255)));
            g.FillRectangle(brush, i % columns * w, i / columns * h, w, h);
        }
        using var border = new Pen(DarkGray);
        g.DrawRectangle(border, 0, 0, 32 * scale - 1, 16 * scale - 1);
        return bitmap;
    }

    /// <summary>Rendu des menus : fond noir, survol cyan, cases cochées magenta.</summary>
    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new Colors()) => RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Gray : e.Item.Selected ? Color.Black : White;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Selected == true ? Color.Black : White;
            base.OnRenderArrow(e);
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Black;
            public override Color ImageMarginGradientBegin => Black;
            public override Color ImageMarginGradientMiddle => Black;
            public override Color ImageMarginGradientEnd => Black;
            public override Color MenuBorder => Cyan;
            public override Color MenuItemBorder => Cyan;
            public override Color MenuItemSelected => Cyan;
            public override Color MenuItemSelectedGradientBegin => Cyan;
            public override Color MenuItemSelectedGradientEnd => Cyan;
            public override Color SeparatorDark => DarkGray;
            public override Color SeparatorLight => Black;
            public override Color CheckBackground => Magenta;
            public override Color CheckSelectedBackground => Magenta;
            public override Color CheckPressedBackground => Magenta;
            public override Color ButtonSelectedBorder => Cyan;
        }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
}
