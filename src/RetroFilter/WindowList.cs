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
