using Whoosh.Core;
namespace Whoosh.Gui;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();Application.Run(new MainForm());
    }
}
public sealed class MainForm:Form
{
    private readonly TextBox output=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",10),BackColor=Color.White};
    private readonly FlowLayoutPanel buttons=new(){Dock=DockStyle.Top,Height=100,Padding=new Padding(12),WrapContents=true};
    private readonly Label status=new(){Text="准备就绪 · 不需要管理员权限",Dock=DockStyle.Bottom,Height=38,Padding=new Padding(12)};
    private bool busy;
    public MainForm()
    {
        Text="MyWhoosh HD 中文管理器 0.1.0";ClientSize=new Size(880,630);MinimumSize=new Size(760,580);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(243,246,248);
        var title=new Label{Text="MyWhoosh HD 中文管理器",Font=new Font(Font.FontFamily,20,FontStyle.Bold),Height=62,Dock=DockStyle.Top,Padding=new Padding(16)};
        var note=new Label{Text="仅支持已验证的 6.2.3.0 构建 · 未知版本拒绝安装 · 安装前先退出游戏",Dock=DockStyle.Top,Height=38,Padding=new Padding(16,0,8,0)};
        AddButton("1  导入汉化资源",()=>Import());AddButton("2  检查安装状态",()=>Execute("check",()=>Operations.Check()));
        AddButton("3  安装 / 更新",()=>Confirm("install","将备份并更新匹配的课程文字与界面补丁。请先关闭游戏。继续？",Operations.Install));
        AddButton("恢复本工具安装",()=>Confirm("restore","仅恢复本工具有备份记录的内容，未知修改会保留。继续？",Operations.Restore));
        AddButton("环境诊断",()=>Execute("diagnose",()=>Operations.Check(true)));
        var center=new Panel{Dock=DockStyle.Fill,Padding=new Padding(16)};center.Controls.Add(output);
        Controls.Add(center);Controls.Add(buttons);Controls.Add(note);Controls.Add(title);Controls.Add(status);
        output.Text="快速开始\r\n\r\n1. 从上游项目获取 v1.1.0 ZIP，点击“导入汉化资源”。\r\n2. 在当前 Windows 账户启动游戏，进入训练列表，随后完全退出。\r\n3. 检查安装状态，再点击安装。\r\n\r\n说明\r\n· 软件不执行第三方安装器脚本或 DLL。\r\n· 只改 Saved 下的补丁与课程文字，不改 WindowsApps。\r\n· 安装后打开游戏确认中文显示。\r\n· 旧安装器已经做好的汉化不会被覆盖，也不会伪造恢复记录；需要恢复旧版时使用原来的恢复程序。\r\n· 本工具独立保存备份，无法恢复并非本工具安装的内容。";
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="操作进行中，请等待结束再关闭。";}};
    }
    private void AddButton(string text,Action action)
    {
        var b=new Button{Text=text,AutoSize=true,Height=38,Padding=new Padding(8),FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(4)};
        b.Click+=(_,_)=>action();buttons.Controls.Add(b);
    }
    private void Import()
    {
        using var dlg=new OpenFileDialog{Title="选择上游汉化资源 ZIP",Filter="ZIP 压缩包|*.zip",CheckFileExists=true};
        if(dlg.ShowDialog(this)!=DialogResult.OK)return;string file=dlg.FileName;
        Execute("import",()=>{PayloadStore.ImportZip(file,AppInfo.Payload);return new{Status="imported",Message="资源导入完成，三项 SHA-256 校验通过。请继续检查。"};});
    }
    private void Confirm(string cmd,string prompt,Func<object> action)
    {
        if(MessageBox.Show(this,prompt,"确认操作",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)==DialogResult.OK)Execute(cmd,action);
    }
    private async void Execute(string cmd,Func<object> action)
    {
        if(busy)return;busy=true;buttons.Enabled=false;status.Text="正在处理，请勿启动游戏或同时运行另一安装器……";
        try
        {
            object result=await Task.Run(action);string log=Operations.Log(cmd,result);output.Text=Operations.Display(result)+"\r\n\r\n本地日志："+log;
            status.Text="操作结束 · 文件检查通过不等于所有游戏页面已验证";
        }
        catch(Exception e){output.Text="操作未完成\r\n"+e.Message;status.Text="已停止，请按上方说明处理；不会强行绕过校验。";}
        finally{buttons.Enabled=true;busy=false;}
    }
}
