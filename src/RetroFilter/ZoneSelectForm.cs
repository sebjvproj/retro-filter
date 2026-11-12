namespace RetroFilter;

/// <summary>
/// Écran de sélection d'une zone : assombrit la zone d'affichage de la fenêtre à filtrer et
/// laisse tracer un rectangle à la souris, comme l'outil Capture de Windows. Le rectangle
/// tracé apparaît « en clair » (troué dans le voile). Échap ou clic droit annule.
/// </summary>
sealed class ZoneSelectForm : Form
{
    /// <summary>Plus petite zone acceptée, en pixels : en dessous, on considère un clic raté.</summary>
    const int MinimumSide = 16;

    // Couleur rendue totalement transparente par Windows (TransparencyKey) : le trou dans le voile.
    static readonly Color Hole = Color.FromArgb(1, 2, 3);

    Point _start;
    Rectangle _current;
    bool _dragging;

    /// <summary>Rectangle choisi, en pixels depuis le coin haut gauche de la zone d'affichage.</summary>
    public Rectangle Selection { get; private set; }

    /// <param name="area">Zone d'affichage de la fenêtre à filtrer, en coordonnées écran.</param>
    public ZoneSelectForm(Rectangle area)
    {
        Text = "retro-filter";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = area;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = 0.6;
        TransparencyKey = Hole;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate(); // pour recevoir Échap
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _start = e.Location;
        _current = Rectangle.Empty;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _current = Between(_start, e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        _current = Between(_start, e.Location);
        if (_current.Width >= MinimumSide && _current.Height >= MinimumSide)
        {
            Selection = _current;
            DialogResult = DialogResult.OK;
            return;
        }
        // Trop petit : on recommence.
        _current = Rectangle.Empty;
        Invalidate();
    }

    /// <summary>Rectangle entre deux points, limité à la zone d'affichage.</summary>
    Rectangle Between(Point a, Point b)
    {
        var rectangle = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        rectangle.Intersect(ClientRectangle);
        return rectangle;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        const TextFormatFlags centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        if (_current.IsEmpty)
        {
            TextRenderer.DrawText(g, "Trace un rectangle à la souris autour de ce que tu veux filtrer.\n\nÉchap pour annuler.",
                RetroStyle.BigFont, ClientRectangle, RetroStyle.Cyan, BackColor, centered);
            return;
        }

        using (var hole = new SolidBrush(Hole))
            g.FillRectangle(hole, _current);
        using (var border = new Pen(RetroStyle.Cyan, 2))
            g.DrawRectangle(border, _current);

        // Taille de la zone, juste sous le rectangle (ou au-dessus s'il touche le bas).
        string size = $"{_current.Width} x {_current.Height}";
        var textSize = TextRenderer.MeasureText(g, size, RetroStyle.BoldFont);
        int y = _current.Bottom + 4 + textSize.Height <= ClientSize.Height ? _current.Bottom + 4 : _current.Top - textSize.Height - 4;
        TextRenderer.DrawText(g, size, RetroStyle.BoldFont, new Point(_current.Left, Math.Max(0, y)), RetroStyle.Cyan, BackColor);
    }
}
