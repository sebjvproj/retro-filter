using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace RetroFilter;

static class GpuDevice
{
    /// <summary>Crée le périphérique Direct3D 11, utilisable depuis plusieurs threads.</summary>
    public static ID3D11Device Create()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device? device).CheckError();
        using (var multithread = device!.QueryInterface<ID3D11Multithread>())
            multithread.SetMultithreadProtected(true);
        return device;
    }
}
