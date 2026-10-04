using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RetroFilter;

/// <summary>
/// Hausse de la priorité GPU de l'app, comme le fait OBS : sans elle, un jeu qui
/// occupe le GPU à 100 % fait passer son travail avant le nôtre et l'affichage saccade.
/// </summary>
static class GpuPriority
{
    // D3DKMT_SCHEDULINGPRIORITYCLASS. « Temps réel » (5) est aussi accepté sans droits
    // administrateur, mais OBS l'évite quand l'ordonnancement GPU matériel est actif.
    const int High = 4;

    /// <summary>Tente d'augmenter la priorité ; renvoie un résumé de ce qui a été obtenu.</summary>
    public static string Raise(ID3D11Device device)
    {
        IntPtr process = Process.GetCurrentProcess().Handle;
        string schedule = D3DKMTSetProcessSchedulingPriorityClass(process, High) == 0 ? "haute" : "normale (refusée)";

        string thread;
        using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            thread = dxgiDevice.SetGPUThreadPriority(7).Success ? "7" : "refusée";

        return $"priorité GPU {schedule}, thread {thread}";
    }

    [DllImport("gdi32.dll")]
    static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priority);
}
