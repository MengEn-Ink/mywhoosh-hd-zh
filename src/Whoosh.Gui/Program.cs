using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Whoosh.Core;

using NativeWindow = Whoosh.Core.NativeWindow;
namespace Whoosh.Gui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Crash("THREAD", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash("DOMAIN", e.ExceptionObject);

        try
        {
            if (args.Length >= 2 && args[0] == "--screenshot")
            {
                using var form = new MainForm();
                form.PrepareForCapture(1280, 860);
                form.SaveShot(args[1], CockpitPage.Overview);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--pages")
            {
                RenderPages(args[1]);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--ui-selftest")
            {
                return SelfTest(args[1]);
            }
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            Crash("STARTUP", ex);
            return 1;
        }
    }

    private static void Crash(string tag, object error)
    {
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "mywhooshzh-crash.log"), tag + ": " + error); } catch { }
    }

    private static readonly (CockpitPage Page, string Name)[] Pages =
    [
        (CockpitPage.Overview, "01-总览"),
        (CockpitPage.Chinese, "02-汉化"),
        (CockpitPage.Window, "03-窗口"),
        (CockpitPage.Restore, "04-恢复"),
    ];

    private static void RenderPages(string dir)
    {
        Directory.CreateDirectory(dir);
        using var form = new MainForm();
        form.PrepareForCapture(1280, 860);
        foreach (var (page, name) in Pages)
        {
            form.SaveShot(Path.Combine(dir, name + ".png"), page);
            Console.WriteLine($"{name}: {form.LastWarnings.Count} warnings, {string.Join(" | ", form.LastWarnings)}");
        }
        form.PrepareForCapture(1100, 760);
        form.SaveShot(Path.Combine(dir, "05-小窗总览.png"), CockpitPage.Overview);
        Console.WriteLine($"小窗: {form.LastWarnings.Count} warnings, {string.Join(" | ", form.LastWarnings)}");
        form.PrepareForCapture(960, 720);
        form.SaveShot(Path.Combine(dir, "06-极窄总览.png"), CockpitPage.Overview);
        Console.WriteLine($"极窄: {form.LastWarnings.Count} warnings, {string.Join(" | ", form.LastWarnings)}");
    }

    private static int SelfTest(string dir)
    {
        Directory.CreateDirectory(dir);
        var pages = new List<object>();
        var warnings = new List<string>();
        using var form = new MainForm();
        form.PrepareForCapture(1280, 860);
        foreach (var (page, name) in Pages)
        {
            string file = Path.Combine(dir, name + ".png");
            form.SaveShot(file, page);
            var pageWarnings = form.LastWarnings.ToArray();
            warnings.AddRange(pageWarnings);
            pages.Add(new { name, file = Path.GetFileName(file), warnings = pageWarnings });
        }
        string small = Path.Combine(dir, "05-小窗总览.png");
        form.PrepareForCapture(1100, 760);
        form.SaveShot(small, CockpitPage.Overview);
        var smallWarnings = form.LastWarnings.ToArray();

        string tiny = Path.Combine(dir, "06-极窄总览.png");
        form.PrepareForCapture(960, 720);
        form.SaveShot(tiny, CockpitPage.Overview);
        var tinyWarnings = form.LastWarnings.ToArray();

        JsonElement? check = null;
        string? checkError = null;
        try { check = JsonSerializer.SerializeToElement(Operations.Check(), AppInfo.JsonOptions); }
        catch (Exception ex) { checkError = ex.Message; }

        var topmost = new { found = false, before = false, afterPin = false, restored = false, handle = 0L };
        IntPtr hwnd = NativeWindow.FindGameWindow("MyWhoosh");
        if (hwnd != IntPtr.Zero)
        {
            bool before = NativeWindow.IsTopmost(hwnd);
            NativeWindow.SetTopmost(hwnd, true);
            Thread.Sleep(150);
            bool afterPin = NativeWindow.IsTopmost(hwnd);
            NativeWindow.SetTopmost(hwnd, before);
            Thread.Sleep(150);
            bool restored = NativeWindow.IsTopmost(hwnd) == before;
            topmost = new { found = true, before, afterPin, restored, handle = hwnd.ToInt64() };
        }

        bool pass = warnings.Count == 0 && smallWarnings.Length == 0 && tinyWarnings.Length == 0
            && check is { } c && c.TryGetProperty("Supported", out var sup) && sup.GetBoolean()
            && (!topmost.found || (topmost.afterPin && topmost.restored));

        var report = new
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            clientSize = "1280x860",
            pages,
            warnings,
            smallView = new { file = Path.GetFileName(small), warnings = smallWarnings },
            tinyView = new { file = Path.GetFileName(tiny), warnings = tinyWarnings },
            check,
            checkError,
            topmost,
            pass,
        };
        File.WriteAllText(Path.Combine(dir, "ui-selftest.json"),
            JsonSerializer.Serialize(report, AppInfo.JsonOptions) + Environment.NewLine);
        Console.WriteLine(JsonSerializer.Serialize(new { pass, warnings = warnings.Count, smallWarnings = smallWarnings.Length, tinyWarnings = tinyWarnings.Length, checkError, topmost }));
        return pass ? 0 : 1;
    }
}

internal sealed class UiState
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 860;

    private static string Path => System.IO.Path.Combine(AppInfo.Home, "ui.json");

    public static UiState Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var state = JsonSerializer.Deserialize<UiState>(File.ReadAllText(Path), AppInfo.JsonOptions);
                if (state is { Width: >= 960, Height: >= 720 }) return state;
            }
        }
        catch { }
        return new UiState();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.Home);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, AppInfo.JsonOptions));
        }
        catch { }
    }
}

internal sealed class MainForm : Form
{
    private readonly CockpitView _view;
    private readonly System.Windows.Forms.Timer _guard = new() { Interval = 2500 };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 5000 };
    private IntPtr _gameHwnd;
    private DateTime _lastLookup = DateTime.MinValue;
    private bool _busy;
    private int _ticks;

    public MainForm()
    {
        Text = "MyWhoosh HD 中文助手 · 竞速座舱";
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Palette.Bg;
        ForeColor = Palette.Ink;
        MinimumSize = new Size(960, 720);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var state = UiState.Load();
        ClientSize = new Size(state.Width, state.Height);

        _view = new CockpitView { Dock = DockStyle.Fill };
        _view.Act += OnAction;
        Controls.Add(_view);

        Load += (_, _) => RefreshState();
        FormClosing += (_, _) =>
        {
            if (WindowState != FormWindowState.Normal) return;
            new UiState { Width = ClientSize.Width, Height = ClientSize.Height }.Save();
        };

        _guard.Tick += (_, _) => GuardTopmost();
        _guard.Start();
        _refresh.Tick += (_, _) => { _ticks++; if (!_busy && _ticks % 4 == 0) { _view.GameRunning = FindGame(); _view.Invalidate(); } };
        _refresh.Start();
    }

    public IReadOnlyList<string> LastWarnings => _view.Warnings;

    public void PrepareForCapture(int width, int height)
    {
        ClientSize = new Size(width, height);
        Show();
        Application.DoEvents();
    }

    public void SaveShot(string path, CockpitPage page)
    {
        _view.Page = page;
        _view.Invalidate();
        Application.DoEvents();
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var bmp = new Bitmap(_view.Width, _view.Height);
        _view.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        bmp.Save(path, ImageFormat.Png);
    }

    // -------------------------------------------------------------- 状态

    private static bool FindGame() => NativeWindow.FindGameWindow("MyWhoosh") != IntPtr.Zero;

    private void RefreshState()
    {
        try
        {
            var t = WindowsTarget.Detect();
            var w = GameWindow.Inspect(t.SavedRoot, AppInfo.State);
            if (w.Width > 0) { _view.WinW = w.Width; _view.WinH = w.Height; }
            _view.Windowed = w.Windowed;
            _view.Topmost = w.Topmost;
            _view.BackupAvailable = w.BackupAvailable;
            _view.PayloadState = PayloadText();
            _view.BackupWhen = BackupText();
            _view.Status = StatusText(new Transactions(t.SavedRoot, AppInfo.State, t.Family).Status());
        }
        catch (Exception ex)
        {
            _view.Status = "无法读取游戏状态";
            if (string.IsNullOrEmpty(_view.Notice)) _view.Notice = ex.Message;
        }
        _view.GameRunning = FindGame();
        _view.Invalidate();
    }

    private static string StatusText(string status) => status switch
    {
        "installed" => "已安装中文",
        "restored" => "已恢复英文",
        "unmanaged" => "未安装",
        "conflict" => "有冲突待处理",
        "prepared" => "上次中断",
        "rolled-back" => "已回滚",
        "unchanged" => "无需重复安装",
        "migrated" => "旧账本已导入",
        _ => status,
    };

    private static string PayloadText()
    {
        try { PayloadStore.Validate(AppInfo.Payload); return "内置 · 校验通过"; }
        catch { return "未释放"; }
    }

    private static string BackupText()
    {
        try
        {
            var times = new List<DateTime>();
            foreach (string name in new[] { "active.json", "window.json" })
            {
                string path = System.IO.Path.Combine(AppInfo.State, name);
                if (File.Exists(path)) times.Add(File.GetLastWriteTime(path));
            }
            if (times.Count == 0) return "";
            var latest = times.Max();
            return latest.Date == DateTime.Today ? $"今天 {latest:HH:mm} · 修改前" : $"{latest:M月d日 HH:mm} · 修改前";
        }
        catch { return ""; }
    }

    private void GuardTopmost()
    {
        try
        {
            if (!_view.Topmost) { _gameHwnd = IntPtr.Zero; return; }
            if (_gameHwnd == IntPtr.Zero && (DateTime.UtcNow - _lastLookup).TotalSeconds > 4)
            {
                _lastLookup = DateTime.UtcNow;
                _gameHwnd = NativeWindow.FindGameWindow("MyWhoosh");
            }
            if (_gameHwnd != IntPtr.Zero && !NativeWindow.SetTopmost(_gameHwnd, true)) _gameHwnd = IntPtr.Zero;
        }
        catch { }
    }

    // -------------------------------------------------------------- 动作分发

    private async void OnAction(string action)
    {
        try
        {
            if (_view.Busy || _busy) return;
            switch (action)
            {
                case "nav:0": case "nav:1": case "nav:2": case "nav:3":
                    _view.Page = (CockpitPage)int.Parse(action[4..]);
                    _view.Invalidate();
                    return;
                case "open-backup": OpenFolder(AppInfo.State); return;
                case "open-log": OpenFolder(System.IO.Path.Combine(AppInfo.Home, "logs")); return;
                case "size:1280:720": case "size:1600:900": case "size:1920:1080": case "size:2560:1440":
                {
                    var parts = action.Split(':');
                    _view.WinW = int.Parse(parts[1]);
                    _view.WinH = int.Parse(parts[2]);
                    _view.Invalidate();
                    return;
                }
                case "check":
                    await Execute("check", "检查", () => Operations.Check());
                    return;
                case "install":
                    if (!Confirm("安装中文界面？", "请先完全退出 MyWhoosh 游戏。\n写入前会备份原始文件，失败会自动回滚，不会修改游戏主资源。")) return;
                    await Execute("install", "安装", () => Operations.Install());
                    return;
                case "apply-window":
                    if (!Confirm("应用窗口设置？", $"将游戏改为窗口模式，尺寸 {_view.WinW} × {_view.WinH}。\n需要先完全退出游戏，改完重新打开生效。")) return;
                    await Execute("window", "窗口", () => GameWindow.Enable(_view.WinW, _view.WinH, _view.Topmost, 2));
                    return;
                case "toggle-topmost":
                    await Execute("topmost", "置顶", () => GameWindow.SetTopmost(!_view.Topmost));
                    return;
                case "restore":
                    if (!Confirm("恢复原版英文？", "只撤销本工具写入的中文界面与课程译文，\n收藏、成绩和之后的训练记录会保留。")) return;
                    await Execute("restore", "恢复", () => Operations.Restore());
                    return;
                case "reset-window":
                    if (!Confirm("恢复窗口设置？", "把游戏的窗口模式与分辨率还原成修改前的样子。\n需要先完全退出游戏。")) return;
                    await Execute("window-reset", "窗口还原", () => GameWindow.Reset());
                    return;
                case "import": await ImportPayload(); return;
                case "migrate":
                    if (!Confirm("导入旧版回退记录？", "把旧安装器留下的备份记录核验后接管，\n不会改动游戏文件。")) return;
                    await Execute("migrate", "导入旧账本", () => LegacyMigration.Current());
                    return;
            }
        }
        catch (Exception ex)
        {
            Error("操作失败", ex);
        }
    }

    private async Task Execute(string key, string label, Func<object> work)
    {
        _view.Busy = true;
        _busy = true;
        _view.Invalidate();
        try
        {
            var element = await Task.Run(() => JsonSerializer.SerializeToElement(work(), AppInfo.JsonOptions));
            ApplyResult(key, label, element);            try { Operations.Log(key, element); } catch { }
        }
        catch (Exception ex)
        {
            try { Operations.Log(key + "-error", new { error = ex.Message }); } catch { }
            AddLog(label + "失败：" + ex.Message);
            Error(label + "失败", ex);
        }
        finally
        {
            _view.Busy = false;
            _busy = false;
            RefreshState();
            _view.Invalidate();
        }
    }

    private void ApplyResult(string key, string label, JsonElement el)
    {
        switch (key)
        {
            case "check":
                _view.Scanned = el.GetProperty("Scanned").GetInt32();
                _view.Matched = el.GetProperty("Matched").GetInt32();
                _view.Already = el.GetProperty("AlreadyChinese").GetInt32();
                _view.Unmatched = el.GetProperty("Unmatched").GetInt32();
                _view.Pending = el.GetProperty("PendingFiles").GetInt32();
                _view.HasStats = true;
                _view.LastCheck = DateTime.Now.ToString("HH:mm");
                _view.Status = "已检查 · 待写入 " + _view.Pending;
                AddLog($"检查：扫描 {_view.Scanned:N0} · 匹配 {_view.Matched:N0} · 待写入 {_view.Pending:N0}");
                break;
            case "install":
            case "restore":
            case "migrate":
            case "payload-import":
                if (el.TryGetProperty("Status", out var status))
                    _view.Status = status.GetString() ?? label;
                else
                    _view.Status = "资源包已导入";
                string changed = el.TryGetProperty("Changed", out var c) ? c.GetInt32().ToString("N0") : "1";
                string message = el.TryGetProperty("Message", out var msg) ? msg.GetString() ?? "" : "";
                AddLog($"{label}：{StatusText(_view.Status)} · 影响 {changed} 个文件 · {message}");
                break;
            case "window":
            case "window-reset":
            case "topmost":
                _view.WinW = el.GetProperty("Width").GetInt32();
                _view.WinH = el.GetProperty("Height").GetInt32();
                _view.Topmost = el.GetProperty("Topmost").GetBoolean();
                _view.BackupAvailable = el.GetProperty("BackupAvailable").GetBoolean();
                _view.Windowed = el.GetProperty("Windowed").GetBoolean();
                _view.Status = key == "topmost"
                    ? (_view.Topmost ? "置顶已开启" : "置顶已关闭")
                    : $"窗口 {_view.WinW}×{_view.WinH}";
                AddLog($"{label}：{_view.WinW}×{_view.WinH} · 置顶 {(_view.Topmost ? "开" : "关")} · {el.GetProperty("Message").GetString()}");
                break;
        }
        _view.Invalidate();
    }

    private async Task ImportPayload()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择汉化语言资源包",
            Filter = "语言资源包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string file = dialog.FileName;
        await Execute("payload-import", "导入资源包", () =>
        {
            PayloadStore.ImportZip(file, AppInfo.Payload);
            return new { Imported = 1, Message = "资源包已校验并写入本地。" };
        });
    }

    private void AddLog(string message)
    {
        _view.Log.Add($"{DateTime.Now:HH:mm}  {message}");
        while (_view.Log.Count > 12) _view.Log.RemoveAt(0);
        _view.Invalidate();
    }

    private bool Confirm(string title, string text) =>
        MessageBox.Show(this, text, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;

    private void Error(string title, Exception ex)
    {
        if (IsHandleCreated && Visible) MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Error("无法打开目录", ex); }
    }
}
