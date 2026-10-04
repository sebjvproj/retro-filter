using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace RetroFilter;

/// <summary>
/// Image filtrée posée exactement sur la zone d'affichage du jeu. Transparente aux clics
/// et jamais active : le jeu garde le clavier et la souris. Cachée quand le jeu n'est pas
/// au premier plan (Alt+Tab vers une autre application), fermée quand le jeu se ferme.
/// </summary>
sealed class OverlayForm : Form
{
    readonly IntPtr _game;
    readonly System.Windows.Forms.Timer _follow = new() { Interval = 15 };
    ID3D11Device? _device;
    Renderer? _renderer;
    WindowCapture? _capture;
    readonly InfoBarForm _bar = new();
    readonly System.Diagnostics.Stopwatch _statsClock = System.Diagnostics.Stopwatch.StartNew();
    Rectangle _bounds;
    bool _shown = true; // Show() rend la fenêtre visible ; Follow() la cache ensuite si besoin.
    readonly FilterState _state;
    string _stats = "";
    string? _message;
    DateTime _messageUntil;

    /// <summary>Pour les tests : reste affiché même si le jeu n'est pas au premier plan.</summary>
    public bool AlwaysVisible { get; init; }

    /// <summary>Barre d'informations affichée en permanence (sinon seulement pour les messages brefs).</summary>
    public bool InfoVisible { get; set; }

    /// <summary>Ligne d'aide de la barre (raccourcis).</summary>
    public string Hint { get; set; } = "";

    public IntPtr Game => _game;

    public OverlayForm(IntPtr game, FilterState state)
    {
        _game = game;
        _state = state;
        Text = "retro-filter";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Bounds = WindowList.GetClientScreenRect(game);
        // Direct3D dessine toute la surface : pas de peinture GDI par-dessus.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Fenêtre « calque » + transparente : les clics passent au travers, jusqu'au jeu.
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);

        _device = GpuDevice.Create();
        GpuPriority.Set(_device, 2);
        _renderer = new Renderer(_device, Handle, ClientSize.Width, ClientSize.Height)
        {
            Crop = WindowList.GetClientRectInCapture(_game),
        };
        _renderer.Configure(_state.Apply);
        _capture = new WindowCapture(_device, _game);
        _capture.FrameArrived += _renderer.SubmitFrame;
        _capture.Closed += () => BeginInvoke(Close);

        _bar.Owner = this; // toujours au-dessus de l'overlay
        _ = _bar.Handle;   // créée maintenant, affichée par Follow()

        _follow.Tick += (_, _) => Follow();
        _follow.Start();
    }

    /// <summary>Suit la position du jeu et décide si l'overlay doit être visible.</summary>
    void Follow()
    {
        if (!WindowList.IsAlive(_game))
        {
            Close();
            return;
        }

        var foreground = WindowList.Foreground;
        bool visible = !WindowList.IsMinimized(_game) && (AlwaysVisible || foreground == _game || foreground == Handle);
        if (visible)
        {
            var bounds = WindowList.GetClientScreenRect(_game);
            if (bounds != _bounds && bounds.Width > 0 && bounds.Height > 0)
            {
                _bounds = bounds;
                _renderer!.Crop = WindowList.GetClientRectInCapture(_game);
                SetWindowPos(Handle, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE);
            }
        }
        if (visible != _shown)
        {
            _shown = visible;
            _renderer!.Paused = !visible;
            ShowWindow(Handle, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
        }
        UpdateInfoBar(visible);
    }

    void UpdateInfoBar(bool overlayVisible)
    {
        // Statistiques recalculées chaque seconde : la capture suit le rythme du jeu,
        // donc ses images par seconde sont aussi celles du jeu.
        if (_statsClock.Elapsed.TotalSeconds >= 1)
        {
            var (frames, average, _) = _renderer!.TakeStats();
            double fps = frames / _statsClock.Elapsed.TotalSeconds;
            _statsClock.Restart();
            _stats = $"{_state.Description}   {fps:F0} i/s   {average:F1} ms";
        }

        bool message = _message != null && DateTime.Now < _messageUntil;
        if (message) _bar.SetText(_message!, InfoVisible ? Hint : "");
        else _bar.SetText(_stats, Hint);
        _bar.Place(new Point(_bounds.X + 8, _bounds.Y + 8), overlayVisible && (InfoVisible || message));
    }

    /// <summary>Affiche un message bref (2 s) dans la barre, même si elle est cachée.</summary>
    public void ShowMessage(string text)
    {
        _message = text;
        _messageUntil = DateTime.Now.AddSeconds(2);
        if (IsHandleCreated) UpdateInfoBar(_shown);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _renderer?.Resize(ClientSize.Width, ClientSize.Height);
    }

    /// <summary>Applique au filtre le choix courant (après un changement de carte, mode ou palette).</summary>
    public void ApplyState()
    {
        _renderer?.Configure(_state.Apply);
        _statsClock.Restart();
        _stats = _state.Description;
    }

    /// <summary>Enregistre la capture brute et l'image filtrée ; renvoie le chemin de l'image filtrée.</summary>
    public string? SaveSnapshot() => _renderer?.SaveSnapshot(FilterModes.SnapshotDirectory, _state.FileLabel);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _follow.Dispose();
        _bar.Dispose();
        _capture?.Dispose();
        _renderer?.Dispose();
        _device?.Dispose();
        base.OnFormClosed(e);
    }

    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_TOPMOST = 0x00000008;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const uint LWA_ALPHA = 0x2;
    const uint SWP_NOACTIVATE = 0x0010;
    const int SW_HIDE = 0;
    const int SW_SHOWNOACTIVATE = 4;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
}
