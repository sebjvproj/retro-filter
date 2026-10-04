using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace RetroFilter;

/// <summary>
/// Capture d'une fenêtre par Windows Graphics Capture.
/// Les images arrivent sur un thread de fond (pool « free threaded »).
/// </summary>
sealed class WindowCapture : IDisposable
{
    const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    const int BufferCount = 3;

    readonly IDirect3DDevice _winrtDevice;
    readonly GraphicsCaptureItem _item;
    readonly Direct3D11CaptureFramePool _framePool;
    readonly GraphicsCaptureSession _session;
    SizeInt32 _lastSize;

    /// <summary>La limite d'images par seconde de Windows a bien été levée.</summary>
    public bool Uncapped { get; }

    /// <summary>Texture de la capture et taille utile de son contenu.</summary>
    public event Action<ID3D11Texture2D, int, int>? FrameArrived;

    /// <summary>La fenêtre capturée a été fermée.</summary>
    public event Action? Closed;

    /// <param name="uncapped">Lève la limite d'images par seconde de Windows (Windows 11 24H2 et plus).</param>
    public WindowCapture(ID3D11Device device, IntPtr hwnd, bool uncapped = true)
    {
        _winrtDevice = CreateWinRTDevice(device);
        _item = CreateItemForWindow(hwnd);
        _item.Closed += (_, _) => Closed?.Invoke();

        _lastSize = _item.Size;
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, PixelFormat, BufferCount, _lastSize);
        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(_item);
        if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.IsCursorCaptureEnabled)))
            _session.IsCursorCaptureEnabled = false;
        if (uncapped && ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.MinUpdateInterval)))
        {
            // Par défaut, Windows espace les images d'environ 1/60 s : on autorise jusqu'au rythme du jeu.
            try
            {
                _session.MinUpdateInterval = TimeSpan.FromMilliseconds(1);
                Uncapped = true;
            }
            catch { }
        }
        if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.IsBorderRequired)))
        {
            // Retire le cadre jaune autour du jeu (Windows 11). Sans effet si Windows refuse.
            try { _session.IsBorderRequired = false; } catch { }
        }
        _session.StartCapture();
    }

    void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame == null) return;

        var size = frame.ContentSize;
        using (var texture = GetTexture(frame.Surface))
        {
            FrameArrived?.Invoke(texture, size.Width, size.Height);
        }

        // La fenêtre a changé de taille : on recrée les tampons à la nouvelle taille.
        if (size.Width != _lastSize.Width || size.Height != _lastSize.Height)
        {
            _lastSize = size;
            sender.Recreate(_winrtDevice, PixelFormat, BufferCount, size);
        }
    }

    public void Dispose()
    {
        _framePool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _framePool.Dispose();
        _winrtDevice.Dispose();
    }

    // --- Interop WinRT / Direct3D 11 ---

    static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    static readonly Guid GraphicsCaptureItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    static readonly Guid DxgiInterfaceAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    static readonly Guid Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    static unsafe GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out IntPtr hstring));
        IntPtr factory;
        try
        {
            Guid iid = GraphicsCaptureItemInteropIid;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, ref iid, out factory));
        }
        finally
        {
            WindowsDeleteString(hstring);
        }

        try
        {
            // IGraphicsCaptureItemInterop::CreateForWindow est la 4e entrée de la vtable (après IUnknown).
            var createForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)factory)[3];
            Guid itemIid = GraphicsCaptureItemIid;
            IntPtr itemPtr;
            Marshal.ThrowExceptionForHR(createForWindow(factory, hwnd, &itemIid, &itemPtr));
            try
            {
                return GraphicsCaptureItem.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    static IDirect3DDevice CreateWinRTDevice(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr ptr));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(ptr);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    static unsafe ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        IntPtr unknown = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            Guid accessIid = DxgiInterfaceAccessIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref accessIid, out IntPtr access));
            try
            {
                // IDirect3DDxgiInterfaceAccess::GetInterface, 4e entrée de la vtable.
                var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)access)[3];
                Guid texIid = Texture2DIid;
                IntPtr texture;
                Marshal.ThrowExceptionForHR(getInterface(access, &texIid, &texture));
                return new ID3D11Texture2D(texture);
            }
            finally
            {
                Marshal.Release(access);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("d3d11.dll")]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
