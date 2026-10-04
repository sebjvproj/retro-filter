using System.Numerics;
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
    readonly ID3D11PixelShader _dither;
    readonly ID3D11PixelShader _ditherFast;
    readonly ID3D11PixelShader _reference;
    readonly ID3D11PixelShader _copy;
    readonly ID3D11SamplerState _linear;
    readonly ID3D11SamplerState _point;
    readonly ID3D11Buffer _params;

    // Palette et mélanges précalculés (DitherTable), refaits quand les réglages concernés changent.
    ID3D11Buffer? _colors, _mixes;
    ID3D11ShaderResourceView? _colorsView, _mixesView;
    int _colorCount;
    (int Palette, bool Luminance, int Levels, float Penalty)? _tableKey;

    // Copie de la dernière image capturée.
    ID3D11Texture2D? _source;
    ID3D11ShaderResourceView? _sourceView;

    // Résultat de la passe CGA, à la résolution virtuelle.
    ID3D11Texture2D? _small;
    ID3D11RenderTargetView? _smallTarget;
    ID3D11ShaderResourceView? _smallView;

    public CgaSettings Settings { get; } = new();
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// En mode pixel 1 : moyenne sur 2x2 lectures au lieu de 8x8. Deux fois moins de temps GPU,
    /// 0,07 % de pixels différents mesurés sur une image de No Man's Sky.
    /// </summary>
    public bool FastSampling { get; set; }

    /// <summary>Pour les tests : utilise cga-v2.hlsl (la v2 de référence) au lieu de dither.hlsl.</summary>
    public bool UseReference { get; set; }
    public bool HasSource => _source != null;
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }

    public CgaFilter(ID3D11Device device)
    {
        _device = device;
        _context = device.ImmediateContext;
        _fullscreen = device.CreateVertexShader(Compile("fullscreen.hlsl", "VSMain", "vs_5_0"));
        _dither = device.CreatePixelShader(Compile("dither.hlsl", "PSMain", "ps_5_0", "#define SAMPLES 8\n"));
        _ditherFast = device.CreatePixelShader(Compile("dither.hlsl", "PSMain", "ps_5_0", "#define SAMPLES 2\n"));
        _reference = device.CreatePixelShader(Compile("cga-v2.hlsl", "PSMain", "ps_5_0", "#define SAMPLES 8\n"));
        _copy = device.CreatePixelShader(Compile("copy.hlsl", "PSMain", "ps_5_0"));
        _linear = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _point = device.CreateSamplerState(SamplerDescription.PointClamp);
        _params = device.CreateBuffer(new BufferDescription(48, BindFlags.ConstantBuffer));
    }

    static byte[] Compile(string file, string entryPoint, string profile, string defines = "")
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", file);
        return Compiler.Compile(defines + File.ReadAllText(path), entryPoint, path, profile).ToArray();
    }

    /// <summary>
    /// Copie l'image capturée : la zone utile (en haut à gauche de la texture),
    /// ou seulement la partie <paramref name="crop"/> de cette zone (sans barre de titre, par exemple).
    /// </summary>
    public void SetSource(ID3D11Texture2D frame, int contentWidth, int contentHeight, Rectangle? crop = null)
    {
        var desc = frame.Description;
        var region = new Rectangle(0, 0, Math.Min(contentWidth, (int)desc.Width), Math.Min(contentHeight, (int)desc.Height));
        if (crop is Rectangle c) region.Intersect(c);
        int w = region.Width, h = region.Height;
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
        _context.CopySubresourceRegion(_source, 0, 0, 0, 0, frame, 0, new Box(region.Left, region.Top, 0, region.Right, region.Bottom, 1));
    }

    /// <summary>Passe CGA : calcule l'image basse résolution.</summary>
    public void Apply()
    {
        if (!Enabled || _sourceView == null) return;

        var (w, h) = Settings.ResolutionFor(SourceWidth, SourceHeight);
        if (_small == null || _small.Description.Width != w || _small.Description.Height != h)
        {
            _smallView?.Dispose();
            _smallTarget?.Dispose();
            _small?.Dispose();
            _small = CreateTexture(w, h, BindFlags.RenderTarget | BindFlags.ShaderResource);
            _smallTarget = _device.CreateRenderTargetView(_small);
            _smallView = _device.CreateShaderResourceView(_small);
        }

        UpdateTable();
        var parameters = Settings.ToParams(w, h, _colorCount);
        _context.UpdateSubresource(in parameters, _params);

        _context.ClearState();
        _context.OMSetRenderTargets(_smallTarget!);
        _context.RSSetViewport(new Viewport(0, 0, w, h));
        _context.PSSetShader(UseReference ? _reference : FastSampling && Settings.PixelSize == 1 ? _ditherFast : _dither);
        _context.PSSetShaderResource(0, _sourceView);
        _context.PSSetShaderResource(1, _colorsView!);
        _context.PSSetShaderResource(2, _mixesView!);
        _context.PSSetSampler(0, _linear);
        _context.PSSetConstantBuffer(0, _params);
        DrawFullscreen();
    }

    void UpdateTable()
    {
        var key = (Settings.Palette, Settings.MatchLuminance, Settings.DitherLevels, Settings.MixPenalty);
        if (_tableKey == key) return;
        _tableKey = key;

        var colors = DitherTable.Colors(Settings);
        var mixes = DitherTable.Mixes(colors, Settings);
        _colorCount = colors.Length;
        _colorsView?.Dispose();
        _colors?.Dispose();
        _mixesView?.Dispose();
        _mixes?.Dispose();
        (_colors, _colorsView) = CreateStructuredBuffer(colors.Select(c => new Vector4(c, 1)).ToArray());
        (_mixes, _mixesView) = CreateStructuredBuffer(mixes);
    }

    (ID3D11Buffer, ID3D11ShaderResourceView) CreateStructuredBuffer(Vector4[] data)
    {
        var buffer = _device.CreateBuffer((ReadOnlySpan<Vector4>)data, new BufferDescription(
            (uint)(data.Length * 16), BindFlags.ShaderResource, ResourceUsage.Immutable,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
        var view = _device.CreateShaderResourceView(buffer, new ShaderResourceViewDescription(buffer, Format.Unknown, 0, (uint)data.Length));
        return (buffer, view);
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

    /// <summary>Attend que le GPU ait fini tout le travail envoyé (pour les mesures).</summary>
    public void WaitForGpu()
    {
        using var query = _device.CreateQuery(new QueryDescription(QueryType.Event));
        _context.End(query);
        while (!_context.IsDataAvailable(query)) Thread.SpinWait(100);
    }

    /// <summary>Enregistre la capture brute.</summary>
    public void SaveSource(string path)
    {
        if (_source == null) return;
        TextureSaver.SavePng(_device, _source, SourceWidth, SourceHeight, path);
    }

    /// <summary>Pixels BGRA de l'image basse résolution (pour les comparaisons).</summary>
    public byte[] ReadSmall()
    {
        var desc = _small!.Description;
        return TextureSaver.ReadPixels(_device, _small, (int)desc.Width, (int)desc.Height);
    }

    /// <summary>Enregistre l'image basse résolution, telle que calculée par la passe CGA.</summary>
    public void SaveSmall(string path)
    {
        if (_small == null) return;
        var desc = _small.Description;
        TextureSaver.SavePng(_device, _small, (int)desc.Width, (int)desc.Height, path);
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
        _colorsView?.Dispose();
        _colors?.Dispose();
        _mixesView?.Dispose();
        _mixes?.Dispose();
        _reference.Dispose();
        _ditherFast.Dispose();
        _dither.Dispose();
        _fullscreen.Dispose();
    }
}
