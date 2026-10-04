using Vortice.Direct3D11;

namespace RetroFilter;

/// <summary>Fenêtre de test : affiche la capture filtrée de la fenêtre choisie.</summary>
sealed class PreviewForm : Form
{
    /// <summary>Dossier des images enregistrées par F12.</summary>
    public static readonly string SnapshotDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "retro-filter");

    readonly WindowInfo _target;
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
        _renderer = new Renderer(_device, Handle, ClientSize.Width, ClientSize.Height);
        _capture = new WindowCapture(_device, _target.Handle);
        _capture.FrameArrived += _renderer.SubmitFrame;
        _capture.Closed += () => BeginInvoke(Close);
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
            case Keys.F12:
                SaveSnapshot();
                break;
        }
    }

    void SaveSnapshot()
    {
        try
        {
            string? path = _renderer?.SaveSnapshot(SnapshotDirectory);
            UpdateTitle(path == null ? "aucune image capturée pour l'instant" : $"enregistré : {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            UpdateTitle($"échec de l'enregistrement : {ex.Message}");
        }
    }

    void UpdateTitle(string? status = null)
    {
        string filter = _renderer?.FilterEnabled == false ? "filtre coupé" : "CGA";
        Text = $"Aperçu : {_target.Title}   [{filter}]   F9 filtre, F12 PNG" + (status == null ? "" : $"   ({status})");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _capture?.Dispose();
        _renderer?.Dispose();
        _device?.Dispose();
        base.OnFormClosed(e);
    }
}
