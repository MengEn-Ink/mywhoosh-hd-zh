namespace Whoosh.Gui;

internal sealed class DesktopContext : ApplicationContext
{
    private readonly MainForm _main;
    private readonly QuickForm _quick;
    private readonly NotifyIcon? _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _signals=new(){Interval=500};
    private readonly EventWaitHandle _open;
    private readonly EventWaitHandle _float;
    private readonly string _home;
    internal MainForm ConsoleForm=>_main;
    internal QuickForm FloatingForm=>_quick;
    internal ContextMenuStrip Menu=>_menu;
    internal string SignalPrefix { get; }
    internal bool Exiting=>_exit;
    private readonly QuickPreferences _prefs;
    private bool _exit;
    internal DesktopContext(bool floating,GuiBackend? backend=null,string? isolatedHome=null)
    {
        bool test=backend?.TestMode==true;
        if(test&&isolatedHome==null)throw new ArgumentException("Test home required");
        _home=isolatedHome??Whoosh.Core.AppInfo.Home;
        SignalPrefix=test?"Local\\MyWhooshHdZh.Test-"+Guid.NewGuid().ToString("N"):"Local\\MyWhooshHdZh";
        _open=new EventWaitHandle(false,EventResetMode.AutoReset,SignalPrefix+".OpenConsole");
        _float=new EventWaitHandle(false,EventResetMode.AutoReset,SignalPrefix+".OpenFloating");
        _prefs=QuickPreferences.Load(_home);
        _main=new MainForm(backend,capture:test);MainForm=_main;_quick=new QuickForm(_main);
        _main.FloatingRequested+=ShowFloating;
        _main.StateChanged+=()=>_quick.RefreshFromMain();
        _main.DialogOwner=()=>{if(_quick.Visible)return _quick;if(!_main.Visible)OpenConsole();return _main;};
        _main.Failure+=message=>{if(!_quick.Visible&&!_main.Visible)_tray?.ShowBalloonTip(4000,"操作未完成",message,ToolTipIcon.Warning);};
        _main.HideToTray=()=>{if(_exit)return false;_main.Hide();return true;};
        _quick.HideRequested+=()=>{_quick.Hide();_prefs.ShowFloating=false;Save();};
        _quick.ConsoleRequested+=OpenConsole;
        _quick.Moved+=()=>{_prefs.X=_quick.Left;_prefs.Y=_quick.Top;Save();};
        _menu=new ContextMenuStrip();
        Add("打开主面板",OpenConsole);Add("显示快捷浮窗",ShowFloating);
        Add("开启 / 关闭游戏置顶",()=>_main.DispatchQuick("toggle-topmost"));
        var sizes=new ToolStripMenuItem("游戏尺寸（退出游戏后应用）");
        foreach(var (w,h) in QuickForm.Sizes)
        {
            var item=new ToolStripMenuItem($"{w} × {h}");item.Click+=(_,_)=>{_main.DispatchQuick($"size:{w}:{h}");_main.DispatchQuick("apply-window");};sizes.DropDownItems.Add(item);
        }
        _menu.Items.Add(sizes);Add("退出助手（停止置顶守护）",Exit);
        if(!test){_tray=new NotifyIcon{Icon=AppIcon.Load(),Text="MyWhoosh 中文助手 · 双击打开",ContextMenuStrip=_menu,Visible=true};_tray.DoubleClick+=(_,_)=>OpenConsole();}
        _main.Show();if(floating)_main.Hide();
        if(floating||_prefs.ShowFloating)ShowFloating();
        _signals.Tick+=(_,_)=>{if(_open.WaitOne(0))OpenConsole();if(_float.WaitOne(0))ShowFloating();};_signals.Start();
    }
    private void Add(string label,Action action){var item=new ToolStripMenuItem(label);item.Click+=(_,_)=>action();_menu.Items.Add(item);}
    private void Save(){try{_prefs.Save(_home);}catch(Exception ex){_tray?.ShowBalloonTip(2500,"浮窗位置未保存",ex.Message,ToolTipIcon.Warning);}}
    private void OpenConsole(){_main.Show();_main.WindowState=FormWindowState.Normal;_main.Activate();}
    private void ShowFloating()
    {
        var wanted=new Point(_prefs.X,_prefs.Y);
        if(_prefs.X==int.MinValue){var r=Screen.PrimaryScreen?.WorkingArea??new Rectangle(0,0,1280,720);wanted=new Point(r.Right-_quick.Width-16,r.Top+32);}
        _quick.Location=QuickPreferences.Fit(wanted,_quick.Size,Screen.AllScreens.Select(s=>s.WorkingArea));
        _quick.Show();_quick.RefreshFromMain();_prefs.ShowFloating=true;Save();
    }
    private void Exit()
    {
        if(_main.IsWorking){_tray?.ShowBalloonTip(2500,"正在处理","请等待当前操作完成后再退出。",ToolTipIcon.Info);return;}
        _exit=true;_main.HideToTray=null;_main.Close();ExitThread();
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){_signals.Stop();_signals.Dispose();if(_tray!=null){_tray.Visible=false;_tray.Icon?.Dispose();_tray.Dispose();}_menu.Dispose();_quick.Dispose();_main.Dispose();_open.Dispose();_float.Dispose();}
        base.Dispose(disposing);
    }
    internal static void Signal(bool floating)
    {
        string name=floating?"Local\\MyWhooshHdZh.OpenFloating":"Local\\MyWhooshHdZh.OpenConsole";
        for(int i=0;i<20;i++)
        {
            if(EventWaitHandle.TryOpenExisting(name,out var handle)){using(handle)handle.Set();return;}
            Thread.Sleep(50);
        }
        MessageBox.Show("助手已在运行，请从系统托盘打开。","MyWhoosh 中文助手");
    }
}
