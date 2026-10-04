using Vortice.Direct3D11;

namespace RetroFilter;

/// <summary>Fenêtre de test : affiche la capture filtrée de la fenêtre choisie.</summary>
sealed class PreviewForm : Form
{
    /// <summary>Dossier des images enregistrées par F12.</summary>
    public static readonly string SnapshotDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "retro-filter");

    // Modes proposés par F8 : nom affiché, nom de fichier, taille des gros pixels (0 = 320x200 fixe).
    static readonly (string Name, string File, int PixelSize)[] Modes =
    [
        ("CGA moderne", "cga-moderne", 1),
        ("pixel 2", "cga-p2", 2),
        ("pixel 3", "cga-p3", 3),
        ("pixel 4", "cga-p4", 4),
        ("CGA historique 320x200", "cga-historique", 0),
    ];

    readonly WindowInfo _target;
    readonly System.Windows.Forms.Timer _fpsTimer = new() { Interval = 1000 };
    int _mode;
    int _fps;
    string? _status;
    ID3D11Device? _device;
    Renderer? _renderer;
    WindowCapture? _capture;

    public PreviewForm(WindowInfo target)
    {
        _target = target;
        UpdateTitle();
        ClientSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        KeyPreview = true;
        // Direct3D dessine toute la surface : pas de peinture GDI par-dessus.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        _device = GpuDevice.Create();
        GpuPriority.Raise(_device);
        _renderer = new Renderer(_device, Handle, ClientSize.Width, ClientSize.Height);
        _capture = new WindowCapture(_device, _target.Handle);
        _capture.FrameArrived += _renderer.SubmitFrame;
        _capture.Closed += () => BeginInvoke(Close);

        _fpsTimer.Tick += (_, _) =>
        {
            _fps = _renderer.TakeFrameCount();
            UpdateTitle();
        };
        _fpsTimer.Start();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized)
            _renderer?.Resize(ClientSize.Width, ClientSize.Height);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                break;
            case Keys.F9:
                if (_renderer == null) break;
                _renderer.FilterEnabled = !_renderer.FilterEnabled;
                UpdateTitle();
                break;
            case Keys.F8:
                if (_renderer == null) break;
                _mode = (_mode + 1) % Modes.Length;
                _renderer.PixelSize = Modes[_mode].PixelSize;
                UpdateTitle();
                break;
            case Keys.F12:
                SaveSnapshot();
                break;
        }
    }

    void SaveSnapshot()
    {
        try
        {
            string? path = _renderer?.SaveSnapshot(SnapshotDirectory, Modes[_mode].File);
            _status = path == null ? "aucune image capturée pour l'instant" : $"enregistré : {Path.GetFileName(path)}";
            UpdateTitle();
        }
        catch (Exception ex)
        {
            _status = $"échec de l'enregistrement : {ex.Message}";
            UpdateTitle();
        }
    }

    void UpdateTitle()
    {
        string filter = _renderer?.FilterEnabled == false ? "filtre coupé" : Modes[_mode].Name;
        Text = $"Aperçu : {_target.Title}   [{filter}]   {_fps} i/s   F8 mode, F9 filtre, F12 PNG" + (_status == null ? "" : $"   ({_status})");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _fpsTimer.Dispose();
        _capture?.Dispose();
        _renderer?.Dispose();
        _device?.Dispose();
        base.OnFormClosed(e);
    }
}
