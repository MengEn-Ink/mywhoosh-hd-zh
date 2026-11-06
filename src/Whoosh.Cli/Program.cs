using Whoosh.Core;

try
{
    if(args.Length==0||args[0] is "--help" or "help")
    {
        Console.WriteLine("MyWhoosh HD ZH "+AppInfo.Version+"\n用法：whoosh-zh <diagnose|check|import|install|restore|window|window-topmost|version>\n汉化资源已内置，首次运行自动释放；import 仅用于覆盖。\nimport --zip <上游资源包.zip>\ninstall --yes    restore --yes\nwindow --width 1280 --height 720 --yes [--topmost]\nwindow-topmost --yes <true|false>\nrecover --yes    window-recover --yes（仅处理中断批次）\npayload-selftest --directory <不存在的新隔离目录>\n未知参数会报错。\n已有旧版汉化由原恢复器管理，本工具不会伪造旧版备份。");return 0;
    }
    string cmd=args[0];object result;
    switch(cmd)
    {
        case "version": if(args.Length!=1)throw new ArgumentException("多余参数。"); Console.WriteLine(AppInfo.Version);return 0;
        case "diagnose": if(args.Length!=1)throw new ArgumentException("多余参数。");result=Operations.Check(true);break;
        case "check": if(args.Length!=1)throw new ArgumentException("多余参数。");result=Operations.Check();break;
        case "import":
            if(args.Length!=3||args[1]!="--zip")throw new ArgumentException("import --zip <文件> 参数不正确。");
            PayloadStore.ImportZip(args[2],AppInfo.Payload);result=new{Status="imported",Message="三项资源哈希通过；没有执行 ZIP 内的脚本或 DLL。"};break;
        case "install":if(args.Length!=2||args[1]!="--yes")throw new ArgumentException("安装必须显式传入 --yes。");result=Operations.Install();break;
        case "restore":if(args.Length!=2||args[1]!="--yes")throw new ArgumentException("恢复必须显式传入 --yes。");result=Operations.Restore();break;
        case "recover":if(args.Length!=2||args[1]!="--yes")throw new ArgumentException("recover --yes required");result=Operations.RecoverPending();break;
        case "window-recover":if(args.Length!=2||args[1]!="--yes")throw new ArgumentException("window-recover --yes required");result=GameWindow.RecoverCurrent();break;
        case "migrate":if(args.Length!=2||args[1]!="--yes")throw new ArgumentException("migrate --yes required");result=LegacyMigration.Current();break;
        case "window-status":if(args.Length!=1)throw new ArgumentException("多余参数。");result=GameWindow.Current();break;
        case "window":
            var window = CliArguments.Window(args);
            result=GameWindow.Enable(window.Width,window.Height,window.Topmost);break;
        case "payload-selftest":
            if(args.Length!=3||args[1]!="--directory")throw new ArgumentException("payload-selftest --directory <新建隔离目录>");
            result=PayloadStore.SelfTest(args[2]);break;
        case "window-topmost":
            if(args.Length!=3||args[1]!="--yes")throw new ArgumentException("window-topmost --yes <true|false>");
            result=GameWindow.SetTopmost(bool.Parse(args[2]));break;
        case "window-restore":if(args.Length!=2||args[1]!="--yes")throw new ArgumentException("window-restore --yes required");result=GameWindow.Reset();break;
        default:throw new ArgumentException("未知命令。");
    }
    if(cmd=="payload-selftest"){Console.WriteLine(Operations.Display(result));return 0;}
    string log=Operations.Log(cmd,result);Console.WriteLine(Operations.Display(result));Console.Error.WriteLine("本地日志："+log);
    return result is OperationResult {Skipped:>0,Status:"conflict"}?3:0;
}
catch(Exception e){Console.Error.WriteLine("操作未完成："+e.Message);return 1;}
