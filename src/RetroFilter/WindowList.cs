using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RetroFilter;

/// <summary>Une fenêtre de premier niveau qu'on peut proposer à la capture.</summary>
sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName)
{
    public override string ToString() => $"{Title}   ({ProcessName})";
}

/// <summary>Liste les fenêtres visibles, à peu près comme Alt+Tab.</summary>
static class WindowList
{
    public static List<WindowInfo> GetCapturableWindows()
    {
        var result = new List<WindowInfo>();
        int ownPid = Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            // Fenêtres « fantômes » des applis UWP suspendues.
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            int length = GetWindowTextLength(hwnd);
            if (length == 0) return true;
            var title = new StringBuilder(length + 1);
            GetWindowText(hwnd, title, title.Capacity);

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) return true;

            result.Add(new WindowInfo(hwnd, title.ToString(), GetProcessName(pid)));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    static string GetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return "?";
        }
    }

    public static bool IsAlive(IntPtr hwnd) => IsWindow(hwnd);

    public static bool IsMinimized(IntPtr hwnd) => IsIconic(hwnd);

    public static IntPtr Foreground => GetForegroundWindow();

    /// <summary>Zone d'affichage (sans barre de titre ni bords) en coordonnées écran.</summary>
    public static Rectangle GetClientScreenRect(IntPtr hwnd)
    {
        GetClientRect(hwnd, out RECT client);
        var origin = new POINT();
        ClientToScreen(hwnd, ref origin);
        return new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
    }

    /// <summary>
    /// Zone d'affichage de la fenêtre, dans l'image capturée. La capture couvre le cadre
    /// visible de la fenêtre (DWMWA_EXTENDED_FRAME_BOUNDS), barre de titre comprise.
    /// </summary>
    public static Rectangle GetClientRectInCapture(IntPtr hwnd)
    {
        var client = GetClientScreenRect(hwnd);
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) != 0)
            GetWindowRect(hwnd, out frame);
        return new Rectangle(client.X - frame.Left, client.Y - frame.Top, client.Width, client.Height);
    }

    /// <summary>Résumé de la géométrie de la fenêtre, pour les tests.</summary>
    public static string DescribeGeometry(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out RECT window);
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>());
        return $"fenêtre {window}, cadre visible {frame}, zone d'affichage {GetClientScreenRect(hwnd)}, dans la capture {GetClientRectInCapture(hwnd)}";
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
        public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Right - Left}x{Bottom - Top}";
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }

    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const uint GW_OWNER = 4;
    const int DWMWA_CLOAKED = 14;

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
