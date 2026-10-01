using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
namespace SoundstageIRGenerator;

// Records real WPF drawing commands offscreen. No visible window or mouse input.
internal static class PlotRenderChecks
{
    internal static void CheckMagnitudeRange()
    {
        var plot=new PlotView{Data=new("Range check","Hz","dB",true,
            [new("normal",[20,1000,20000],[0,4,0],"#52DBBF"),
             new("peak",[20,1000,20000],[0,47,0],"#EAB66E")],-60,20)};
        plot.Measure(new Size(900,560));plot.Arrange(new Rect(0,0,900,560));
        var render=typeof(PlotView).GetMethod("OnRender",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var full=typeof(PlotView).GetField("fullBounds",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var current=typeof(PlotView).GetField("bounds",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var visual=new DrawingVisual();
        PlotBounds Draw(){using(var dc=visual.RenderOpen())render.Invoke(plot,[dc]);return (PlotBounds)full.GetValue(plot)!;}
        var expanded=Draw();
        if(expanded.YMax!=50||expanded.YMin!=-60)throw new Exception("Magnitude peak is clipped or lower bound changed");
        plot.ToggleLine("peak");if(Draw().YMax!=20)throw new Exception("Hidden peak still expands magnitude axis");
        plot.ShowAll();Draw();plot.Zoom(.5);Draw();plot.ResetView();Draw();
        if(((PlotBounds)current.GetValue(plot)!).YMax!=50)throw new Exception("Reset does not restore expanded magnitude range");
        if(plot.GeometryBuildCount!=2)throw new Exception("Axis fitting rebuilt curve geometry");
        plot.Data=plot.Data with{Title="Decay",XUnit="ms",LogX=false,MaxY=0,MinY=-80};
        if(Draw().YMax!=0)throw new Exception("Magnitude fitting changed decay axis");
    }
    internal static string Measure(PlotData data)
    {
        var plot=new PlotView{Data=data};
        plot.Measure(new Size(900,560));plot.Arrange(new Rect(0,0,900,560));plot.UpdateLayout();
        var render=typeof(PlotView).GetMethod("OnRender",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var viewport=typeof(PlotView).GetField("viewport",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var bounds=typeof(PlotView).GetField("fullBounds",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var visual=new DrawingVisual();
        void Draw(){using var dc=visual.RenderOpen();render.Invoke(plot,[dc]);}
        Draw();var full=(PlotBounds)bounds.GetValue(plot)!;
        var zoom=full.Zoom(.45,.5,.5);double span=full.XMax-full.XMin;
        var elapsed=new List<double>();long allocated=0;
        for(int i=0;i<125;i++)
        {
            viewport.SetValue(plot,zoom.Pan(Math.Sin(i*.12)*span*.2,0).Constrain(full));
            long before=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();Draw();clock.Stop();
            if(i>=5){elapsed.Add(clock.Elapsed.TotalMilliseconds);allocated+=GC.GetAllocatedBytesForCurrentThread()-before;}
        }
        elapsed.Sort();
        if(plot.GeometryBuildCount!=data.Lines.Count)throw new Exception("Panning rebuilt curve sample geometry");
        if(allocated/120>100000)throw new Exception($"Panning allocated {allocated/120} bytes/frame (limit 100 KB); median {elapsed[60]:0.000} ms");
        return $"MEASURE plot pan recording {data.Title}: median {elapsed[60]:0.000} ms, p95 {elapsed[114]:0.000} ms, {allocated/120} bytes/frame ({data.Lines.Sum(l=>l.X.Length)} points)";
    }
}
