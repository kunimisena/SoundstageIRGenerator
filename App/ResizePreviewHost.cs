using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace SoundstageIRGenerator;

// During a native window size/move loop, retain the whole page at its previous
// layout size and scale its cached surface. Release schedules one normal reflow.
// Keeping the whole page stable also avoids text wrapping and adaptive-grid work.
public sealed class ResizePreviewHost:Decorator
{
    readonly Decorator surface=new();
    readonly ScaleTransform previewScale=new();
    readonly BitmapCache previewCache=new(){EnableClearType=false,SnapsToDevicePixels=true};
    Size layoutSize;
    bool sizeMoveLoop;
    DispatcherOperation? pendingRefresh;
    HwndSource? source;
    internal bool PreviewActive {get;private set;}
    internal bool NativeHookAttached=>source!=null;
    internal int LiveArrangeCount {get;private set;}
    internal int PreviewArrangeCount {get;private set;}
    internal int CompletedRefreshCount {get;private set;}
    internal Size StableLayoutSize=>layoutSize;
    internal UIElement? Page=>surface.Child;
    internal bool IsReflowDeferred=>PreviewActive||sizeMoveLoop;

    public ResizePreviewHost()
    {
        Child=surface;ClipToBounds=true;
        surface.RenderTransform=previewScale;
        // Keep a ready-to-scale surface: entering resize performs no synchronous
        // RenderTargetBitmap capture or redraw of thousands of curve segments.
        surface.CacheMode=previewCache;
    }
    internal static ResizePreviewHost Install(Window window)
    {
        if(window.Content is ResizePreviewHost existing)return existing;
        var page=(UIElement)window.Content;window.Content=null;
        var host=new ResizePreviewHost();host.surface.Child=page;window.Content=host;
        void Connect(object? sender,EventArgs e)
        {
            if(host.source!=null)return;
            var handle=new WindowInteropHelper(window).Handle;
            if(handle==IntPtr.Zero)return;
            host.source=HwndSource.FromHwnd(handle);host.source?.AddHook(host.WindowMessage);
        }
        window.SourceInitialized+=Connect;Connect(null,EventArgs.Empty);
        window.Closed+=(_,_)=>
        {
            window.SourceInitialized-=Connect;
            host.pendingRefresh?.Abort();host.pendingRefresh=null;
            if(host.source is {IsDisposed:false})host.source.RemoveHook(host.WindowMessage);
            host.source=null;
        };
        return host;
    }
    IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x0231) // WM_ENTERSIZEMOVE: also sent for title-bar movement.
        {
            pendingRefresh?.Abort();pendingRefresh=null;sizeMoveLoop=true;
        }
        else if(message is 0x0232 or 0x001f) // WM_EXITSIZEMOVE / WM_CANCELMODE
        {
            sizeMoveLoop=false;
            if(PreviewActive)
            {
                pendingRefresh?.Abort();
                pendingRefresh=Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>
                {
                    pendingRefresh=null;if(sizeMoveLoop)return;
                    PreviewActive=false;previewScale.ScaleX=previewScale.ScaleY=1;
                    CompletedRefreshCount++;
                    // Arrange and chart painting happen after the native sizing
                    // callback has returned, using the latest final window size.
                    surface.InvalidateMeasure();InvalidateMeasure();
                });
            }
        }
        return IntPtr.Zero;
    }
    protected override Size MeasureOverride(Size available)
    {
        bool changed=double.IsFinite(available.Width)&&double.IsFinite(available.Height)&&available!=layoutSize;
        if(sizeMoveLoop&&changed&&layoutSize.Width>0&&layoutSize.Height>0)PreviewActive=true;
        if(PreviewActive)return new Size(double.IsFinite(available.Width)?available.Width:layoutSize.Width,double.IsFinite(available.Height)?available.Height:layoutSize.Height);
        surface.Measure(available);return surface.DesiredSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if(PreviewActive)
        {
            PreviewArrangeCount++;
            previewScale.ScaleX=finalSize.Width/layoutSize.Width;
            previewScale.ScaleY=finalSize.Height/layoutSize.Height;
            return finalSize;
        }
        LiveArrangeCount++;previewScale.ScaleX=previewScale.ScaleY=1;
        layoutSize=finalSize;surface.Arrange(new Rect(finalSize));return finalSize;
    }
}
