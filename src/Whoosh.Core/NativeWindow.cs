using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Whoosh.Core;

/// <summary>
/// 最小化的 Win32 窗口互操作：按进程定位 MyWhoosh 主窗口，并切换置顶。
/// 不引入第三方依赖，只使用 user32.dll 中稳定的基础函数。
/// </summary>
public static class NativeWindow
{
    private const int GW_OWNER = 4;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>查找属于指定进程且可见的顶层窗口句柄。</summary>
    public static IntPtr FindMainWindow(int processId)
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != (uint)processId) return true;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
            if (!IsWindowVisible(hWnd)) return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>按进程名前缀查找游戏主窗口。</summary>
    public static IntPtr FindGameWindow(string processPrefix)
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (!p.ProcessName.StartsWith(processPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (p.ProcessName.Equals("MyWhooshZH", StringComparison.OrdinalIgnoreCase)) continue;
                var h = FindMainWindow(p.Id);
                if (h != IntPtr.Zero) return h;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>设置窗口置顶状态。返回是否成功。</summary>
    public static bool SetTopmost(IntPtr hWnd, bool topmost)
    {
        if (hWnd == IntPtr.Zero || !OperatingSystem.IsWindows()) return false;
        return SetWindowPos(hWnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>获取窗口标题。</summary>
    public static string GetTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "";
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
