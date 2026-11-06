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
                using var form = new MainForm(capture:true);
                form.PrepareForCapture(1280, 860);
                form.SaveShot(args[1], CockpitPage.Overview);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--pages")
            {
                RenderPages(args[1]);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--layout-selftest")
            {
                return SelfTest(args[1], layoutOnly: true);
            }
            if (args.Length >= 2 && args[0] == "--ui-selftest")
            {
                return SelfTest(args[1]);
            }
            if(args.Length==2&&args[0]=="--quick-selftest")return QuickTests.Run(args[1]);
            if(args.Length==2&&args[0]=="--performance-selftest")return PerformanceTests.Run(args[1]);
            if(args.Length==2&&args[0]=="--readonly-responsiveness")return PerformanceTests.ReadOnly(args[1]);
            bool floating=args.Length==1&&args[0]=="--floating";
            if(args.Length>0&&!floating)throw new ArgumentException("未知GUI参数。");
            using var mutex=new Mutex(true,"Local\\MyWhooshHdZh.Desktop",out bool first);
            if(!first){DesktopContext.Signal(floating);return 0;}
            try{using var context=new DesktopContext(floating);Application.Run(context);}
            finally{mutex.ReleaseMutex();}
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
        using var form = new MainForm(capture:true);
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

    private static int SelfTest(string dir, bool layoutOnly = false)
    {
        Directory.CreateDirectory(dir);
        object? payloadTest=layoutOnly?PayloadStore.SelfTest(Path.Combine(dir,"isolated-payload")):null;
        var pages = new List<object>();
        var warnings = new List<string>();
        using var form = new MainForm(layoutOnly?GuiBackend.LayoutFixture():null,capture:true);
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

        var detailViews=new List<object>();
        foreach(var (page,name) in Pages.Where(p=>p.Page!=CockpitPage.Overview))
        {
            string file="窄窗-"+name+".png";form.SaveShot(Path.Combine(dir,file),page);
            var wa=form.LastWarnings.ToArray();warnings.AddRange(wa);detailViews.Add(new{file,warnings=wa});
        }
        var stressViews=new List<object>();
        if(layoutOnly)
        {
            form.View.Notice=string.Concat(Enumerable.Repeat("中断恢复遇到后续修改，请保留备份并查看完整记录。",8));
            form.View.Status="中断恢复有冲突，未知修改已保留";
            for(int i=0;i<5;i++)form.View.Log.Add(string.Concat(Enumerable.Repeat("窗口配置与原记录不一致，未覆盖用户修改。",10)));
            foreach(int dpi in new[]{96,120,144,192})
            {
                form.View.CaptureDpi=dpi;
                form.PrepareForCapture(dpi==96?960:(int)(960*dpi/96f),dpi==96?720:(int)(720*dpi/96f));
                foreach(var (page,name) in Pages)
                {
                    string file=$"压力-{dpi}-"+name+".png";form.SaveShot(Path.Combine(dir,file),page);
                    var wa=form.LastWarnings.ToArray();warnings.AddRange(wa);stressViews.Add(new{dpi,actualWidth=form.View.Width,actualHeight=form.View.Height,file,warnings=wa});
                }
            }
            form.View.CaptureDpi=null;
        }
        JsonElement? check = null;
        string? checkError = null;
        if (!layoutOnly)
        {
            try { check = JsonSerializer.SerializeToElement(Operations.Check(), AppInfo.JsonOptions); }
            catch (Exception ex) { checkError = ex.Message; }
        }

        var topmost = new { found = false, before = false, afterPin = false, restored = false, handle = 0L };
        IntPtr hwnd = layoutOnly ? IntPtr.Zero : NativeWindow.FindGameWindow("MyWhoosh");
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

        var interactions=JsonSerializer.SerializeToElement(InteractionTests.Run(),AppInfo.JsonOptions);
        bool interactionPass=interactions.GetProperty("pass").GetBoolean();
        bool pass = interactionPass && warnings.Count == 0 && smallWarnings.Length == 0 && tinyWarnings.Length == 0
            && (layoutOnly || (check is { } c && c.TryGetProperty("Supported", out var sup) && sup.GetBoolean()))
            && (!topmost.found || (topmost.afterPin && topmost.restored));

        var report = new
        {
            mode = layoutOnly ? "layout-only" : "game-integration",
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            clientSize = "1280x860",
            pages,
            detailViews,
            stressViews,
            payloadTest,
            dpiTest="simulated layout metrics; not a physical monitor transition test",
            warnings,
            smallView = new { file = Path.GetFileName(small), warnings = smallWarnings },
            tinyView = new { file = Path.GetFileName(tiny), warnings = tinyWarnings },
            check,
            checkError,
            interactions,
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
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private bool _runtimeBusy;
    private bool _loadingState;
    private long _runtimeEpoch;
    private readonly object _runtimeGate=new();
    private bool _busy;
    private bool _sizeDraft;
    private bool _checkPassed;

    private readonly GuiBackend _backend;
    internal bool IsWorking => _busy || _loadingState;
    internal event Action? StateChanged;
    internal event Action? FloatingRequested;
    internal event Action<string>? Failure;
    internal Func<IWin32Window?>? DialogOwner { get; set; }
    internal Func<bool>? Confirmation { get; set; }
    internal Func<bool>? HideToTray { get; set; }
    internal void DispatchQuick(string action)=>OnAction(action);
    internal CockpitView View => _view;
    private readonly bool _capture;
    public MainForm(GuiBackend? backend = null,bool capture = false)
    {
        _capture=capture;
        _backend=backend??new GuiBackend();
        Text = "MyWhoosh HD 中文助手 · 竞速座舱";
        Icon=AppIcon.Load();
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Palette.Bg;
        ForeColor = Palette.Ink;
        MinimumSize = new Size(960, 720);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var state = _capture || _backend.TestMode ? new UiState() : UiState.Load();
        ClientSize = new Size(state.Width, state.Height);

        _view = new CockpitView { Dock = DockStyle.Fill };
        _view.Act += OnAction;
        Controls.Add(_view);

        Load += async (_, _) => await RefreshStateAsync();
        FormClosing += (_, e) =>
        {
            if(IsWorking){e.Cancel=true;return;}
            if(e.CloseReason==CloseReason.UserClosing && HideToTray!=null)
            {
                if(!_backend.TestMode&&!_capture&&WindowState==FormWindowState.Normal)new UiState{Width=ClientSize.Width,Height=ClientSize.Height}.Save();
                if(HideToTray.Invoke()){e.Cancel=true;return;}
            }
            if (_backend.TestMode || _capture || WindowState != FormWindowState.Normal) return;
            new UiState { Width = ClientSize.Width, Height = ClientSize.Height }.Save();
        };

        _refresh.Tick += async (_, _) => await RefreshRuntimeAsync();
        if(!_backend.TestMode && !_capture)_refresh.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){Interlocked.Increment(ref _runtimeEpoch);_refresh.Stop();_refresh.Dispose();}
        base.Dispose(disposing);
    }
    public IReadOnlyList<string> LastWarnings => _view.Warnings;

    public void PrepareForCapture(int width, int height)
    {
        ClientSize = new Size(width, height);
        Show();
        var deadline=DateTime.UtcNow.AddSeconds(12);
        while(IsWorking&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(5);}
        Application.DoEvents();
        if(IsWorking)throw new TimeoutException("截图初始状态读取超时。");
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

    internal void RefreshForTest()=>ApplySnapshot(_backend.Snapshot());
    internal async Task RefreshStateAsync()
    {
        if(_loadingState||IsDisposed)return;
        _loadingState=true;_view.Busy=true;StateChanged?.Invoke();
        try{var snapshot=await Task.Run(_backend.Snapshot);if(!IsDisposed)ApplySnapshot(snapshot);}
        catch(Exception ex)
        {
            if(IsDisposed)return;
            _view.Status="无法读取游戏状态";_view.Supported=false;_view.BackupAvailable=false;_view.HasStats=false;
            _view.Notice=ex.Message;_view.InstallInterrupted=false;_view.WindowInterrupted=false;
        }
        finally
        {
            _loadingState=false;
            if(!IsDisposed){_view.Busy=_busy;StateChanged?.Invoke();_view.Invalidate();}
        }
    }
    private void ApplySnapshot(UiSnapshot s)
    {
            var w=s.Window;
        if (!_checkPassed || !_view.Supported || _view.GameVersion!=s.Version)_view.HasStats=false;
            _view.GameVersion=s.Version;_view.Supported=s.Supported;
            if (!_sizeDraft && w.Width > 0) { _view.WinW = w.Width; _view.WinH = w.Height; }
            _view.Windowed=w.Windowed;_view.Topmost=w.Topmost;
            _view.WindowBackupAvailable=w.BackupAvailable;
            _view.BackupAvailable=s.CanRestore;
            _view.PayloadState=s.Payload;
            _view.BackupWhen=s.CreatedUtc is {} utc?$"{utc.ToLocalTime():M月d日 HH:mm} · 首次备份":s.CanRestore?"旧记录 · 时间未知":"";
            _view.Status=StatusText(s.Journal);
            _view.InstallInterrupted=s.Journal=="prepared";
            _view.WindowInterrupted=w.Message.StartsWith("窗口批次",StringComparison.Ordinal);
            _view.Notice=_view.InstallInterrupted?"上次汉化中断：退出游戏，点处理中断记录。已提交的汉化不会全部撤销。":_view.WindowInterrupted?"上次窗口操作中断：退出游戏，点处理窗口中断。":s.Supported?_view.Notice:"构建未支持，仅可诊断";
        StateChanged?.Invoke();_view.Invalidate();
    }
    internal async Task RefreshRuntimeAsync()
    {
        if(_runtimeBusy||IsDisposed)return;
        _runtimeBusy=true;long epoch=Interlocked.Read(ref _runtimeEpoch);
        bool pin=!_busy&&!_loadingState&&_view.Topmost&&!_view.WindowInterrupted;
        try
        {
            var snapshot=await Task.Run(()=>{lock(_runtimeGate){return _backend.ReadRuntime(pin&&epoch==Interlocked.Read(ref _runtimeEpoch));}});
            if(IsDisposed||epoch!=Interlocked.Read(ref _runtimeEpoch))return;
            bool changed=_view.GameRunning!=snapshot.Running||_view.ActualTopmost!=snapshot.Topmost||_view.ActualSize!=snapshot.Size;
            _view.GameRunning=snapshot.Running;_view.ActualTopmost=snapshot.Topmost;_view.ActualSize=snapshot.Size;
            if(changed){StateChanged?.Invoke();if(_view.Visible)_view.Invalidate();}
        }
        catch(Exception ex)
        {
            if(!IsDisposed){_view.ActualTopmost=null;_view.ActualSize="暂不可读";_view.Notice="窗口状态读取失败："+ex.Message;StateChanged?.Invoke();}
        }
        finally{_runtimeBusy=false;}
    }

    private static string StatusText(string status) => status switch
    {
        "installed" => "有本工具备份",
        "restored" => "已恢复英文",
        "unmanaged" => "无本工具记录",
        "conflict" => "有冲突待处理",
        "prepared" => "上次中断",
        "rolled-back" => "已回滚",
        "unchanged" => "无需重复安装",
        "unknown" => "构建未支持",
        "migrated" => "旧账本已导入",
        _ => status,
    };

    // Runtime monitoring is single-flight and off the UI thread; pin only when style is absent.

    // -------------------------------------------------------------- 动作分发

    private async void OnAction(string action)
    {
        try
        {
            if (_view.Busy || _busy || _loadingState)
            {
                if(action.StartsWith("nav:",StringComparison.Ordinal)){_view.Page=(CockpitPage)int.Parse(action[4..]);_view.Invalidate();}
                if(action=="floating")FloatingRequested?.Invoke();
                return;
            }
            switch (action)
            {
                case "nav:0": case "nav:1": case "nav:2": case "nav:3":
                    _view.Page = (CockpitPage)int.Parse(action[4..]);
                    _view.Invalidate();
                    return;
                case "floating":
                    FloatingRequested?.Invoke();return;
                case "help":
                    if(!_backend.TestMode)MessageBox.Show(this,"装中文：先退出游戏，点检查，再点安装 / 更新。\n新课程：退出游戏后再检查，新增内容可增量更新。\n还显示英文：核对同一账户、游戏构建、课程匹配，再重启。\n小窗：选预设尺寸，应用后重启；拖边能力由游戏决定。\n置顶：保持助手运行；看实际置顶，不只看开关。\n回英文：保留备份，恢复只撤销已记录的汉化。\n\nWorkouts：单次课程；Training Plans：多周计划。\nPower Source：功率来源；Controllable：可控阻力。\nERG：目标功率控制；FTP：训练功率基准。\nSave / Upload：保存 / 上传；Discard：丢弃。\n\n分享问题时只提供脱敏诊断，不上传存档或整个 AppData。", "上手帮助与术语", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                case "show-details":
                    if(!_backend.TestMode)
                    {
                        using var dialog=new Form{Text="完整提示与操作记录",Size=new Size(800,560),MinimumSize=new Size(480,320),StartPosition=FormStartPosition.CenterParent};
                        using var text=DetailsText();dialog.Controls.Add(text);dialog.ShowDialog(this);
                    }
                    return;
                case "recover-install":
                    if(!Confirm("处理中断记录？","先退出游戏。仅处理上次未完成批次，已提交的汉化保留。\n未知冲突会保留现场，不强行删除。"))return;
                    await Execute("recover-install","处理中断",_backend.RecoverInstall);return;
                case "recover-window":
                    if(!Confirm("处理窗口中断？","先退出游戏。仅处理上次未完成窗口批次，不还原所有窗口设置。"))return;
                    await Execute("recover-window","处理窗口中断",_backend.RecoverWindow);return;
                case "open-backup": OpenFolder(AppInfo.State); return;
                case "open-log": OpenFolder(System.IO.Path.Combine(AppInfo.Home, "logs")); return;
                case "size:1280:720": case "size:1600:900": case "size:1920:1080": case "size:2560:1440":
                {
                    var parts = action.Split(':');
                    _sizeDraft=true;
                    _view.WinW = int.Parse(parts[1]);
                    _view.WinH = int.Parse(parts[2]);
                    _view.Invalidate();StateChanged?.Invoke();
                    return;
                }
                case "check":
                    await Execute("check", "检查", _backend.Check);
                    return;
                case "install":
                    if(!_view.Supported){_view.Notice="请先检查受支持的游戏构建";_view.Invalidate();return;}
                    if (!Confirm("安装中文界面？", "请先完全退出 MyWhoosh 游戏。\n写入前会备份原始文件，失败会自动回滚，不会修改游戏主资源。")) return;
                    await Execute("install", "安装", _backend.Install);
                    return;
                case "apply-window":
                    if (!Confirm("应用窗口设置？", $"将游戏改为窗口模式，尺寸 {_view.WinW} × {_view.WinH}。\n需要先完全退出游戏，改完重新打开生效。")) return;
                    await Execute("window", "窗口", () => _backend.ApplyWindow(_view.WinW, _view.WinH, _view.Topmost));
                    return;
                case "toggle-topmost":
                    await Execute("topmost", "置顶", () => _backend.SetTopmost(!_view.Topmost));
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
        StateChanged?.Invoke();
        _view.Invalidate();
        try
        {
            int width=_view.WinW,height=_view.WinH;bool pinBefore=_view.Topmost,desiredPin=!pinBefore;
            Interlocked.Increment(ref _runtimeEpoch);
            var element = await Task.Run(() =>
            {
                // A runtime read cannot race a user's explicit pin change.
                object result;
                if(key=="topmost"){lock(_runtimeGate){result=_backend.SetTopmost(desiredPin);}}
                else result=key=="window"?_backend.ApplyWindow(width,height,pinBefore):work();
                return JsonSerializer.SerializeToElement(result,AppInfo.JsonOptions);
            });
            if(IsDisposed)return;
            ApplyResult(key, label, element);
            try { if(!_backend.TestMode) await Task.Run(()=>Operations.Log(key,element)); } catch { }
            // Window/pin results already contain state. Never revalidate thousands of backups here.
            if(key is not ("topmost" or "window" or "window-reset" or "recover-window"))await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            _view.HasStats=false;_checkPassed=false;
            try { if(!_backend.TestMode) await Task.Run(()=>Operations.Log(key+"-error",new {error=ex.Message})); } catch { }
            AddLog(label + "失败：" + ex.Message);
            Error(label + "失败", ex);
            if(key!="topmost")await RefreshStateAsync();
        }
        finally
        {
            _view.Busy = false;
            _busy = false;
            if(!IsDisposed){StateChanged?.Invoke();_view.Invalidate();await RefreshRuntimeAsync();}
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
                _checkPassed=el.TryGetProperty("Supported",out var sup) && sup.GetBoolean();_view.Supported=_checkPassed;
                if(el.TryGetProperty("GameVersion",out var gv))_view.GameVersion=gv.GetString()??"未检测";
                _view.Page=CockpitPage.Overview;
                _view.Notice=_view.Pending>0?"检查完成，点安装 / 更新中文":"检查完成，无需重复安装";
                AddLog($"检查：扫描 {_view.Scanned:N0} · 匹配 {_view.Matched:N0} · 待写入 {_view.Pending:N0}");
                break;
            case "install":
            case "restore":
            case "recover-install":
                _view.HasStats=false;_checkPassed=false;
                goto case "migrate";
            case "migrate":
            case "payload-import":
                if (el.TryGetProperty("Status", out var status))
                    _view.Status = status.GetString() ?? label;
                else
                    _view.Status = "资源包已导入";
                string changed = el.TryGetProperty("Changed", out var c) ? c.GetInt32().ToString("N0") : "1";
                string message = el.TryGetProperty("Message", out var msg) ? msg.GetString() ?? "" : "";
                _view.Notice=message;
                AddLog($"{label}：{StatusText(_view.Status)} · 影响 {changed} 个文件 · {message}");
                break;
            case "window":
            case "window-reset":
            case "recover-window":
            case "topmost":
                if(key!="topmost")
                {
                    _sizeDraft=false;
                    _view.WinW = el.GetProperty("Width").GetInt32();
                    _view.WinH = el.GetProperty("Height").GetInt32();
                }
                _view.Topmost = el.GetProperty("Topmost").GetBoolean();
                _view.WindowBackupAvailable = el.GetProperty("BackupAvailable").GetBoolean();
                _view.Windowed = el.GetProperty("Windowed").GetBoolean();
                if(key=="recover-window")_view.WindowInterrupted=false;
                _view.Status = key == "topmost"
                    ? (_view.Topmost ? "置顶已开启" : "置顶已关闭")
                    : $"窗口 {_view.WinW}×{_view.WinH}";
                _view.Notice=el.GetProperty("Message").GetString()??"";
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

    internal TextBox DetailsText()=>new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Both,Text=_view.Notice+Environment.NewLine+Environment.NewLine+string.Join(Environment.NewLine+Environment.NewLine,_view.Log)};
    private void AddLog(string message)
    {
        _view.Log.Add($"{DateTime.Now:HH:mm}  {message}");
        while (_view.Log.Count > 12) _view.Log.RemoveAt(0);
        _view.Invalidate();
    }

    private bool Confirm(string title, string text) =>
        Confirmation?.Invoke() ?? (_backend.TestMode || MessageBox.Show(DialogOwner?.Invoke()??this, text, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK);

    private void Error(string title, Exception ex)
    {
        _view.Notice=title+"："+ex.Message;StateChanged?.Invoke();Failure?.Invoke(_view.Notice);
        var owner=DialogOwner?.Invoke();
        if (!_backend.TestMode && IsHandleCreated && (Visible||owner!=null)) MessageBox.Show(owner??this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
