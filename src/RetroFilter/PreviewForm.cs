using Vortice.Direct3D11;

namespace RetroFilter;

/// <summary>Fenêtre de test : affiche la capture de la fenêtre choisie.</summary>
sealed class PreviewForm : Form
{
    readonly WindowInfo _target;
    ID3D11Device? _device;
    Renderer? _renderer;
    WindowCapture? _capture;

    public PreviewForm(WindowInfo target)
    {
        _target = target;
        Text = $"Aperçu : {target.Title}";
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
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _capture?.Dispose();
        _renderer?.Dispose();
        _device?.Dispose();
        base.OnFormClosed(e);
    }
}
