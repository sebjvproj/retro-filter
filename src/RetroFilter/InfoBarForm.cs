using System.Runtime.InteropServices;

namespace RetroFilter;

/// <summary>
/// Petite barre d'informations semi-transparente posée sur le jeu (mode, images par seconde,
/// messages brefs). Transparente aux clics et jamais active, comme l'overlay. Elle n'apparaît
/// pas dans l'image filtrée : la capture ne voit que la fenêtre du jeu.
/// </summary>
sealed class InfoBarForm : Form
{
    static readonly Font MainFont = new("Segoe UI", 10f, FontStyle.Bold);
    static readonly Font HintFont = new("Segoe UI", 8.5f);
    const int TextMargin = 8;

    string _main = "";
    string _hint = "";

    public InfoBarForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(16, 16, 24);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, 215, LWA_ALPHA);
    }

    /// <summary>Change le texte (ligne principale et ligne d'aide) et ajuste la taille.</summary>
    public void SetText(string main, string hint)
    {
        if (main == _main && hint == _hint) return;
        _main = main;
        _hint = hint;
        var a = TextRenderer.MeasureText(main, MainFont);
        var b = hint.Length == 0 ? Size.Empty : TextRenderer.MeasureText(hint, HintFont);
        ClientSize = new Size(Math.Max(a.Width, b.Width) + 2 * TextMargin, a.Height + b.Height + TextMargin + (hint.Length == 0 ? 0 : 2));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        TextRenderer.DrawText(e.Graphics, _main, MainFont, new Point(TextMargin, TextMargin / 2), Color.White);
        if (_hint.Length > 0)
        {
            int y = TextMargin / 2 + TextRenderer.MeasureText(_main, MainFont).Height + 2;
            TextRenderer.DrawText(e.Graphics, _hint, HintFont, new Point(TextMargin, y), Color.FromArgb(170, 170, 190));
        }
    }

    /// <summary>Place la barre (coin haut gauche) et l'affiche ou la cache, sans jamais l'activer.</summary>
    public void Place(Point location, bool visible)
    {
        if (visible)
        {
            SetWindowPos(Handle, HWND_TOPMOST, location.X, location.Y, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE);
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
        }
        else
        {
            ShowWindow(Handle, SW_HIDE);
        }
    }

    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_TOPMOST = 0x00000008;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const uint LWA_ALPHA = 0x2;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;
    const int SW_HIDE = 0;
    const int SW_SHOWNOACTIVATE = 4;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
}
