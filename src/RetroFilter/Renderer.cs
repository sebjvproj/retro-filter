using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace RetroFilter;

/// <summary>
/// Dessine la dernière image capturée dans une fenêtre, en gardant ses proportions.
/// Appelé à la fois par le thread de capture et par le thread de l'interface : tout passe par un verrou.
/// </summary>
sealed class Renderer : IDisposable
{
    readonly object _sync = new();
    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly IDXGISwapChain1 _swapChain;
    readonly ID3D11VertexShader _vertexShader;
    readonly ID3D11PixelShader _pixelShader;
    readonly ID3D11SamplerState _sampler;

    ID3D11RenderTargetView? _renderTarget;
    int _width, _height;

    // Copie de la dernière image capturée, pour pouvoir redessiner sans nouvelle image.
    ID3D11Texture2D? _source;
    ID3D11ShaderResourceView? _sourceView;
    int _sourceWidth, _sourceHeight;

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

        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", "passthrough.hlsl");
        string source = File.ReadAllText(path);
        _vertexShader = device.CreateVertexShader(CompileShader(source, path, "VSMain", "vs_5_0"));
        _pixelShader = device.CreatePixelShader(CompileShader(source, path, "PSMain", "ps_5_0"));
        _sampler = device.CreateSamplerState(SamplerDescription.PointClamp);
    }

    static byte[] CompileShader(string source, string path, string entryPoint, string profile)
    {
        return Compiler.Compile(source, entryPoint, path, profile).ToArray();
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
            var desc = frame.Description;
            int w = Math.Min(contentWidth, (int)desc.Width);
            int h = Math.Min(contentHeight, (int)desc.Height);
            if (w <= 0 || h <= 0) return;

            if (_source == null || w != _sourceWidth || h != _sourceHeight)
            {
                _sourceView?.Dispose();
                _source?.Dispose();
                _source = _device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)w,
                    Height = (uint)h,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                });
                _sourceView = _device.CreateShaderResourceView(_source);
                _sourceWidth = w;
                _sourceHeight = h;
            }

            _context.CopySubresourceRegion(_source, 0, 0, 0, 0, frame, 0, new Box(0, 0, 0, w, h, 1));
            Draw();
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

            // Draw() repositionne tout l'état : on peut le vider pour libérer le tampon d'affichage.
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

        if (_sourceView != null)
        {
            // Bandes noires pour garder les proportions de l'image capturée.
            float scale = Math.Min((float)_width / _sourceWidth, (float)_height / _sourceHeight);
            float w = _sourceWidth * scale;
            float h = _sourceHeight * scale;
            _context.RSSetViewport(new Viewport((_width - w) / 2, (_height - h) / 2, w, h));

            _context.OMSetRenderTargets(_renderTarget!);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.IASetInputLayout(null);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetShaderResource(0, _sourceView);
            _context.PSSetSampler(0, _sampler);
            _context.Draw(3, 0);
        }

        _swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _context.ClearState();
            _sourceView?.Dispose();
            _source?.Dispose();
            _renderTarget?.Dispose();
            _sampler.Dispose();
            _pixelShader.Dispose();
            _vertexShader.Dispose();
            _swapChain.Dispose();
            _context.Dispose();
        }
    }
}
