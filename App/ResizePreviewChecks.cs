using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace SoundstageIRGenerator;

// Hidden HWND notifications + full client-tree resizing, not chart panning.
internal static class ResizePreviewChecks
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    internal static async Task Run(Window window,Action<bool,string> check,List<string> log,Action<FrameworkElement,string,double,double>? capture=null)
    {
        var host=(ResizePreviewHost)window.Content;
        var handle=new WindowInteropHelper(window).EnsureHandle();
        check(!window.IsVisible&&host.NativeHookAttached,"Resize hook attached to a hidden native window");
        async Task Frame(double width,double height=820)
        {
            host.LayoutTransform=Transform.Identity;
            host.Measure(new Size(width,height));host.Arrange(new Rect(0,0,width,height));host.UpdateLayout();
            await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        }
        static IEnumerable<PlotView> Plots(DependencyObject item)
        {
            if(item is PlotView plot)yield return plot;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)foreach(var found in Plots(VisualTreeHelper.GetChild(item,i)))yield return found;
        }
        await Frame(1280);
        var plots=Plots(host).ToArray();
        check(plots.Length>0&&plots.Any(p=>p.Data!=null),"Resize uses populated result plots");
        int paints=plots.Sum(p=>p.DrawCount),layouts=host.LiveArrangeCount,previews=host.PreviewArrangeCount,completed=host.CompletedRefreshCount;
        var size=host.StableLayoutSize;
        SendMessage(handle,0x0231,IntPtr.Zero,IntPtr.Zero);
        // The native title-bar move shares ENTER/EXIT notifications but needs no preview.
        await Frame(1280);
        check(!host.PreviewActive,"Title-bar movement without a size change does not freeze the page");
        var clock=Stopwatch.StartNew();long allocations=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<90;i++)await Frame(900+(i%30)*18,820+(i%5)*2);
        await Frame(900,820);clock.Stop();
        allocations=GC.GetAllocatedBytesForCurrentThread()-allocations;
        check(host.PreviewActive&&host.StableLayoutSize==size,"Whole page keeps its original layout while window width changes");
        check(host.LiveArrangeCount==layouts&&host.PreviewArrangeCount>=90,"Ninety resize frames use cached scaling with zero live page arrangements");
        check(plots.Sum(p=>p.DrawCount)==paints,"Window resizing repaints zero detailed plots during the drag");
        check(host.RenderSize.Width==900,"Cached preview follows current window width");
        capture?.Invoke(host,"resize-preview.png",900,820);
        SendMessage(handle,0x0232,IntPtr.Zero,IntPtr.Zero);
        check(host.PreviewActive&&host.CompletedRefreshCount==completed,"Mouse-release notification returns before final layout work");
        await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        await Frame(900,820);
        check(!host.PreviewActive&&host.CompletedRefreshCount==completed+1&&host.StableLayoutSize.Width==900,"Release refreshes once at the final size");
        check(plots.Sum(p=>p.DrawCount)>paints,"Release restores sharp charts at the final dimensions");
        capture?.Invoke(host,"resize-settled.png",900,820);
        log.Add($"MEASURE whole-window resize: 91 frames, {clock.Elapsed.TotalMilliseconds:0.00} ms including dispatcher yields, {allocations} allocated bytes; 0 live arrangements and 0 plot paints during drag");
        // A second drag can begin before the deferred refresh from the first runs.
        SendMessage(handle,0x0231,IntPtr.Zero,IntPtr.Zero);await Frame(1100);
        SendMessage(handle,0x0232,IntPtr.Zero,IntPtr.Zero);
        SendMessage(handle,0x0231,IntPtr.Zero,IntPtr.Zero);await Frame(1200);
        check(host.PreviewActive&&host.CompletedRefreshCount==completed+1,"A new resize supersedes the queued refresh");
        SendMessage(handle,0x001f,IntPtr.Zero,IntPtr.Zero); // WM_CANCELMODE
        await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Frame(1200);
        check(!host.PreviewActive&&host.CompletedRefreshCount==completed+2,"Cancel restores a normally laid-out page");
        await Frame(1280);
        check(!window.IsVisible,"Resize validation never shows the window");
    }
}
