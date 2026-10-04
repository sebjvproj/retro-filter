using Vortice.Direct3D11;

namespace RetroFilter;

/// <summary>Fenêtre de test : affiche la capture filtrée de la fenêtre choisie.</summary>
sealed class PreviewForm : Form
{
    static readonly (string Name, string File, int PixelSize)[] Modes = FilterModes.All;

    readonly WindowInfo _target;
    readonly System.Windows.Forms.Timer _fpsTimer = new() { Interval = 1000 };
    int _mode;
    int _priority = 2;
    string _stats = "";
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
        GpuPriority.Set(_device, _priority);
        _renderer = new Renderer(_device, Handle, ClientSize.Width, ClientSize.Height);
        _capture = new WindowCapture(_device, _target.Handle);
        _capture.FrameArrived += _renderer.SubmitFrame;
        _capture.Closed += () => BeginInvoke(Close);

        _fpsTimer.Tick += (_, _) =>
        {
            var (frames, average, worst) = _renderer.TakeStats();
            _stats = $"{frames} i/s, traitement {average:F1} ms (pire {worst:F0})";
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
            case Keys.F6:
                if (_renderer == null) break;
                _renderer.FastSampling = !_renderer.FastSampling;
                UpdateTitle();
                break;
            case Keys.F7:
                if (_device == null) break;
                _priority = (_priority + 1) % GpuPriority.Levels.Length;
                _status = GpuPriority.Set(_device, _priority) ? null : "priorité refusée par Windows";
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
            string? path = _renderer?.SaveSnapshot(FilterModes.SnapshotDirectory, "cga-" + Modes[_mode].File);
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
        string filter = _renderer?.FilterEnabled == false ? "filtre coupé" : "CGA " + Modes[_mode].Name;
        if (_renderer?.FilterEnabled == true && Modes[_mode].PixelSize == 1)
            filter += _renderer.FastSampling ? ", 2x2 lectures" : ", 8x8 lectures";
        Text = $"Aperçu [{filter}]   {_stats}   GPU {GpuPriority.Levels[_priority].Name}   F6 lectures, F7 priorité, F8 mode, F9 filtre, F12 PNG"
            + (_status == null ? "" : $"   ({_status})");
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
