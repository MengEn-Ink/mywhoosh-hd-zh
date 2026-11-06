using Whoosh.Core;

namespace Whoosh.Gui;

internal sealed record UiSnapshot(string Version,bool Supported,string Journal,bool CanRestore,DateTime? CreatedUtc,WindowState Window,string Payload);
internal sealed class GuiBackend
{
    internal Func<object> Check { get; init; } = () => Operations.Check();
    internal Func<object> Install { get; init; } = () => Operations.Install();
    internal Func<UiSnapshot> Snapshot { get; init; } = Read;
    internal bool TestMode { get; init; }
    internal static GuiBackend LayoutFixture() => new()
    {
        TestMode=true,
        Snapshot=()=>new UiSnapshot(AppInfo.GameVersion,true,"unmanaged",false,null,new WindowState(true,1280,720,false,false,"仅布局测试"),"内置 · 待释放")
    };
    private static UiSnapshot Read()
    {
        var t=WindowsTarget.Detect(true);
        bool supported=t.Name==AppInfo.PackageName && t.Family==AppInfo.Family && t.Version==AppInfo.GameVersion;
        // Identity readiness is separate from the full hash compatibility gate used by Check/Install.
        var w=supported?GameWindow.Inspect(t.SavedRoot,AppInfo.State):new WindowState(false,0,0,false,false,"未支持此构建");
        var summary=supported?new Transactions(t.SavedRoot,AppInfo.State,t.Family).Summary():("unknown",false,(DateTime?)null,0);
        string payload;
        try{PayloadStore.Validate(AppInfo.Payload);payload="内置 · 校验通过";}catch{payload="内置 · 待释放";}
        DateTime? date=summary.Item3;
        // Never substitute a window backup timestamp for the Chinese-install baseline timestamp.
        return new(t.Version,supported,summary.Item1,summary.Item2,date,w,payload);
    }
}
