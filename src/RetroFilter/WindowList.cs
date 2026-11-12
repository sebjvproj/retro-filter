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

    public static bool IsVisible(IntPtr hwnd) => IsWindowVisible(hwnd);

    public static bool IsTopmost(IntPtr hwnd) => (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;

    /// <summary>La fenêtre appartient à retro-filter lui-même.</summary>
    public static bool IsOwn(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == Environment.ProcessId;
    }

    /// <summary>
    /// L'overlay est-il juste au-dessus de la fenêtre dans l'ordre d'empilement ? On ignore ce
    /// qui ne se voit pas (fenêtres cachées) et nos propres fenêtres auxiliaires. Sinon,
    /// <paramref name="insertAfter"/> est la fenêtre derrière laquelle le replacer (zéro = tout en haut).
    /// </summary>
    public static bool IsJustAbove(IntPtr hwnd, IntPtr overlay, out IntPtr insertAfter)
    {
        insertAfter = IntPtr.Zero;
        bool found = false;
        for (IntPtr h = GetWindow(hwnd, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
        {
            if (h == overlay) return true;
            if (IsOwn(h)) continue;
            if (!found)
            {
                insertAfter = h;
                found = true;
            }
            if (IsWindowVisible(h)) return false;
        }
        return false;
    }

    /// <summary>Met la fenêtre au premier plan (et la restaure si elle est réduite).</summary>
    public static void Activate(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    /// <summary>Une application est en plein écran exclusif (l'overlay ne peut pas s'afficher par-dessus).</summary>
    public static bool ExclusiveFullscreenRunning =>
        SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;

    /// <summary>Petite icône de la fenêtre, ou null. Windows reste propriétaire de l'icône d'origine.</summary>
    public static Bitmap? GetIcon(IntPtr hwnd)
    {
        try
        {
            IntPtr icon = IntPtr.Zero;
            foreach (int kind in new[] { ICON_SMALL2, ICON_SMALL, ICON_BIG })
            {
                if (SendMessageTimeout(hwnd, WM_GETICON, kind, IntPtr.Zero, SMTO_ABORTIFHUNG, 50, out icon) != IntPtr.Zero && icon != IntPtr.Zero) break;
                icon = IntPtr.Zero;
            }
            if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, GCLP_HICONSM);
            if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, GCLP_HICON);
            if (icon == IntPtr.Zero) return null;
            using var wrapper = Icon.FromHandle(icon);
            return wrapper.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

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

    /// <summary>Pour les tests : les fenêtres empilées au-dessus de celle donnée, de la plus proche à la plus haute.</summary>
    public static string DescribeAbove(IntPtr hwnd, int count)
    {
        var lines = new List<string>();
        for (IntPtr h = GetWindow(hwnd, GW_HWNDPREV); h != IntPtr.Zero && lines.Count < count; h = GetWindow(h, GW_HWNDPREV))
        {
            var name = new StringBuilder(256);
            GetClassName(h, name, name.Capacity);
            var title = new StringBuilder(256);
            GetWindowText(h, title, title.Capacity);
            GetWindowThreadProcessId(h, out uint pid);
            lines.Add($"  0x{h:X} classe {name} « {title} » pid {pid} visible {IsWindowVisible(h)} possédée par 0x{GetWindow(h, GW_OWNER):X}");
        }
        return string.Join("\n", lines);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);

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
    const long WS_EX_TOPMOST = 0x00000008;
    const uint GW_HWNDPREV = 3;
    const uint GW_OWNER = 4;
    const int SW_RESTORE = 9;
    const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    const uint WM_GETICON = 0x007F;
    const int ICON_SMALL = 0, ICON_BIG = 1, ICON_SMALL2 = 2;
    const uint SMTO_ABORTIFHUNG = 0x0002;
    const int GCLP_HICON = -14, GCLP_HICONSM = -34;

    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, nint wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
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
