using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace RetroFilter;

/// <summary>
/// Affiche la capture filtrée dans une fenêtre, en gardant ses proportions.
/// Appelé à la fois par le thread de capture et par le thread de l'interface : tout passe par un verrou.
/// </summary>
sealed class Renderer : IDisposable
{
    readonly object _sync = new();
    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly IDXGISwapChain1 _swapChain;
    readonly CgaFilter _filter;

    ID3D11RenderTargetView? _renderTarget;
    int _width, _height;
    int _framesSinceLastRead;

    /// <summary>Nombre d'images capturées et affichées depuis le dernier appel (remis à zéro).</summary>
    public int TakeFrameCount() => Interlocked.Exchange(ref _framesSinceLastRead, 0);

    public Renderer(ID3D11Device device, IntPtr hwnd, int width, int height)
    {
        _device = device;
        _context = device.ImmediateContext;
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);

        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();
        _swapChain = factory.CreateSwapChainForHwnd(device, hwnd, new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
        });
        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
        CreateRenderTarget();

        _filter = new CgaFilter(device);
    }

    void CreateRenderTarget()
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(backBuffer);
    }

    /// <summary>Reçoit une image capturée (thread de capture).</summary>
    public void SubmitFrame(ID3D11Texture2D frame, int contentWidth, int contentHeight)
    {
        lock (_sync)
        {
            _filter.SetSource(frame, contentWidth, contentHeight);
            _filter.Apply();
            Draw();
            Interlocked.Increment(ref _framesSinceLastRead);
        }
    }

    public bool FilterEnabled
    {
        get { lock (_sync) return _filter.Enabled; }
        set
        {
            lock (_sync)
            {
                _filter.Enabled = value;
                _filter.Apply();
                Draw();
            }
        }
    }

    /// <summary>Taille des gros pixels ; 0 = résolution fixe 320x200.</summary>
    public int PixelSize
    {
        get { lock (_sync) return _filter.Settings.PixelSize; }
        set
        {
            lock (_sync)
            {
                _filter.Settings.PixelSize = value;
                _filter.Apply();
                Draw();
            }
        }
    }

    /// <summary>Enregistre la capture brute et l'image filtrée ; renvoie le chemin de l'image filtrée.</summary>
    public string? SaveSnapshot(string directory, string label)
    {
        lock (_sync)
        {
            if (!_filter.HasSource) return null;
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string output = Path.Combine(directory, $"{stamp}_{label}.png");
            _filter.SaveSource(Path.Combine(directory, $"{stamp}_source.png"));
            _filter.SaveOutput(output);
            return output;
        }
    }

    /// <summary>La fenêtre d'affichage a changé de taille (thread de l'interface).</summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        lock (_sync)
        {
            if (width == _width && height == _height) return;
            _width = width;
            _height = height;

            _context.ClearState();
            _renderTarget?.Dispose();
            _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None);
            CreateRenderTarget();
            Draw();
        }
    }

    void Draw()
    {
        _context.ClearRenderTargetView(_renderTarget!, new Color4(0, 0, 0, 1));

        if (_filter.HasSource)
        {
            // Échelle entière (1:1 au minimum, bords rognés si la fenêtre est trop petite) :
            // une réduction détruirait le tramage fin. Image centrée, bandes noires autour.
            float fit = Math.Min((float)_width / _filter.SourceWidth, (float)_height / _filter.SourceHeight);
            float scale = Math.Max(1, MathF.Floor(fit));
            float w = _filter.SourceWidth * scale;
            float h = _filter.SourceHeight * scale;
            _filter.DrawOutput(_renderTarget!, new Viewport((_width - w) / 2, (_height - h) / 2, w, h));
        }

        _swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _filter.Dispose();
            _renderTarget?.Dispose();
            _swapChain.Dispose();
            _context.Dispose();
        }
    }
}
