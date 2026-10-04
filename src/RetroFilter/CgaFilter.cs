using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace RetroFilter;

/// <summary>
/// Chaîne de rendu sur GPU : copie de la capture, passe CGA à basse résolution,
/// puis agrandissement sans lissage vers une cible quelconque.
/// N'est pas protégée contre les accès concurrents : l'appelant s'en charge.
/// </summary>
sealed class CgaFilter : IDisposable
{
    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly ID3D11VertexShader _fullscreen;
    readonly ID3D11PixelShader _cga;
    readonly ID3D11PixelShader _copy;
    readonly ID3D11SamplerState _linear;
    readonly ID3D11SamplerState _point;
    readonly ID3D11Buffer _params;

    // Copie de la dernière image capturée.
    ID3D11Texture2D? _source;
    ID3D11ShaderResourceView? _sourceView;

    // Résultat de la passe CGA, à la résolution virtuelle.
    ID3D11Texture2D? _small;
    ID3D11RenderTargetView? _smallTarget;
    ID3D11ShaderResourceView? _smallView;

    public CgaSettings Settings { get; } = new();
    public bool Enabled { get; set; } = true;
    public bool HasSource => _source != null;
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }

    public CgaFilter(ID3D11Device device)
    {
        _device = device;
        _context = device.ImmediateContext;
        _fullscreen = device.CreateVertexShader(Compile("fullscreen.hlsl", "VSMain", "vs_5_0"));
        _cga = device.CreatePixelShader(Compile("cga-v2.hlsl", "PSMain", "ps_5_0"));
        _copy = device.CreatePixelShader(Compile("copy.hlsl", "PSMain", "ps_5_0"));
        _linear = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _point = device.CreateSamplerState(SamplerDescription.PointClamp);
        _params = device.CreateBuffer(new BufferDescription(48, BindFlags.ConstantBuffer));
    }

    static byte[] Compile(string file, string entryPoint, string profile)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", file);
        return Compiler.Compile(File.ReadAllText(path), entryPoint, path, profile).ToArray();
    }

    /// <summary>Copie l'image capturée (seule la zone utile, en haut à gauche).</summary>
    public void SetSource(ID3D11Texture2D frame, int contentWidth, int contentHeight)
    {
        var desc = frame.Description;
        int w = Math.Min(contentWidth, (int)desc.Width);
        int h = Math.Min(contentHeight, (int)desc.Height);
        if (w <= 0 || h <= 0) return;

        if (_source == null || w != SourceWidth || h != SourceHeight)
        {
            _sourceView?.Dispose();
            _source?.Dispose();
            _source = CreateTexture(w, h, BindFlags.ShaderResource);
            _sourceView = _device.CreateShaderResourceView(_source);
            SourceWidth = w;
            SourceHeight = h;
        }
        _context.CopySubresourceRegion(_source, 0, 0, 0, 0, frame, 0, new Box(0, 0, 0, w, h, 1));
    }

    /// <summary>Passe CGA : calcule l'image basse résolution.</summary>
    public void Apply()
    {
        if (!Enabled || _sourceView == null) return;

        int w = Settings.Width, h = Settings.Height;
        if (_small == null || _small.Description.Width != w || _small.Description.Height != h)
        {
            _smallView?.Dispose();
            _smallTarget?.Dispose();
            _small?.Dispose();
            _small = CreateTexture(w, h, BindFlags.RenderTarget | BindFlags.ShaderResource);
            _smallTarget = _device.CreateRenderTargetView(_small);
            _smallView = _device.CreateShaderResourceView(_small);
        }

        var parameters = Settings.ToParams();
        _context.UpdateSubresource(in parameters, _params);

        _context.ClearState();
        _context.OMSetRenderTargets(_smallTarget!);
        _context.RSSetViewport(new Viewport(0, 0, w, h));
        _context.PSSetShader(_cga);
        _context.PSSetShaderResource(0, _sourceView);
        _context.PSSetSampler(0, _linear);
        _context.PSSetConstantBuffer(0, _params);
        DrawFullscreen();
    }

    /// <summary>Dessine le résultat (ou la capture brute si le filtre est coupé) dans la zone donnée.</summary>
    public void DrawOutput(ID3D11RenderTargetView target, Viewport viewport)
    {
        var view = Enabled && _smallView != null ? _smallView : _sourceView;
        if (view == null) return;

        _context.ClearState();
        _context.OMSetRenderTargets(target);
        _context.RSSetViewport(viewport);
        _context.PSSetShader(_copy);
        _context.PSSetShaderResource(0, view);
        _context.PSSetSampler(0, _point);
        DrawFullscreen();
    }

    void DrawFullscreen()
    {
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_fullscreen);
        _context.Draw(3, 0);
    }

    /// <summary>Enregistre la capture brute.</summary>
    public void SaveSource(string path)
    {
        if (_source == null) return;
        TextureSaver.SavePng(_device, _source, SourceWidth, SourceHeight, path);
    }

    /// <summary>Enregistre l'image basse résolution, telle que calculée par la passe CGA.</summary>
    public void SaveSmall(string path)
    {
        if (_small == null) return;
        TextureSaver.SavePng(_device, _small, Settings.Width, Settings.Height, path);
    }

    /// <summary>Enregistre l'image affichée, agrandie à la taille de la capture.</summary>
    public void SaveOutput(string path)
    {
        if (_source == null) return;
        using var texture = CreateTexture(SourceWidth, SourceHeight, BindFlags.RenderTarget);
        using var target = _device.CreateRenderTargetView(texture);
        DrawOutput(target, new Viewport(0, 0, SourceWidth, SourceHeight));
        TextureSaver.SavePng(_device, texture, SourceWidth, SourceHeight, path);
    }

    ID3D11Texture2D CreateTexture(int width, int height, BindFlags bindFlags) =>
        _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = bindFlags,
        });

    public void Dispose()
    {
        _context.ClearState();
        _smallView?.Dispose();
        _smallTarget?.Dispose();
        _small?.Dispose();
        _sourceView?.Dispose();
        _source?.Dispose();
        _params.Dispose();
        _point.Dispose();
        _linear.Dispose();
        _copy.Dispose();
        _cga.Dispose();
        _fullscreen.Dispose();
    }
}
