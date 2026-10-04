using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RetroFilter;

/// <summary>
/// Priorité GPU de l'app, comme le fait OBS : sans elle, un jeu qui occupe le GPU
/// à 100 % fait passer son travail avant le nôtre et l'affichage saccade.
/// </summary>
static class GpuPriority
{
    /// <summary>Niveaux proposés (valeurs de D3DKMT_SCHEDULINGPRIORITYCLASS).</summary>
    public static readonly (string Name, int Value)[] Levels =
    [
        ("normale", 2),
        ("haute", 4),
        ("temps réel", 5),
    ];

    /// <summary>Applique le niveau donné (index dans Levels) ; renvoie false si Windows refuse.</summary>
    public static bool Set(ID3D11Device device, int level)
    {
        using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            dxgiDevice.SetGPUThreadPriority(level == 0 ? 0 : 7);
        return D3DKMTSetProcessSchedulingPriorityClass(Process.GetCurrentProcess().Handle, Levels[level].Value) == 0;
    }

    [DllImport("gdi32.dll")]
    static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priority);
}
