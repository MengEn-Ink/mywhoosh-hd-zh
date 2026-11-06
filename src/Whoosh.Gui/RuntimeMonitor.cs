using Whoosh.Core;
using NativeWindow=Whoosh.Core.NativeWindow;

namespace Whoosh.Gui;

// Single-flight caller runs this off the UI thread. Never reads payloads or backup journals.
internal sealed class RuntimeMonitor
{
    private IntPtr _window;
    private DateTime _lastSearch=DateTime.MinValue;
    internal RuntimeSnapshot Read(bool pin)
    {
        if(_window!=IntPtr.Zero&&!NativeWindow.IsGameWindow(_window))_window=IntPtr.Zero;
        if(_window==IntPtr.Zero&&(DateTime.UtcNow-_lastSearch).TotalMilliseconds>=750)
        {
            _lastSearch=DateTime.UtcNow;_window=NativeWindow.FindGameWindow("MyWhoosh");
        }
        if(_window==IntPtr.Zero)return new(false,null,"未启动");
        bool topmost=NativeWindow.IsTopmost(_window);
        if(pin&&!topmost){NativeWindow.SetTopmost(_window,true);topmost=NativeWindow.IsTopmost(_window);}
        var size=NativeWindow.ClientSize(_window);
        return new(true,topmost,size is {} s?$"{s.Width} × {s.Height}":"读取中");
    }
}
