using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
internal static class Paint
{
    [ThreadStatic] public static double PixelsPerDip;
    public static readonly Brush Text=new SolidColorBrush(Color.FromRgb(57,53,45)),Muted=new SolidColorBrush(Color.FromRgb(111,104,91)),Grid=new SolidColorBrush(Color.FromRgb(217,208,189)),Accent=new SolidColorBrush(Color.FromRgb(66,107,113));
    public static readonly Brush Paper=new SolidColorBrush(Color.FromRgb(246,240,225));
    static readonly Typeface Font=new("Segoe UI");
    static readonly Dictionary<(string,double,Color,double,string),(FormattedText Layout,DrawingGroup Drawing)> labels=[];
    static readonly Queue<(string,double,Color,double,string)> labelOrder=[];
    static (FormattedText Layout,DrawingGroup Drawing) TextDrawing(string text,Brush? brush,double size)
    {
        brush??=Text;double dpi=PixelsPerDip>0?PixelsPerDip:1;
        var key=(text,size,((SolidColorBrush)brush).Color,dpi,CultureInfo.CurrentCulture.Name);
        if(labels.TryGetValue(key,out var cached))return cached;
        if(labels.Count>=2048)labels.Remove(labelOrder.Dequeue());
        var label=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Font,size,brush,dpi);
        var drawing=new DrawingGroup();using(var dc=drawing.Open())dc.DrawText(label,new(0,0));drawing.Freeze();
        var entry=(label,drawing);labels.Add(key,entry);labelOrder.Enqueue(key);return entry;
    }
    public static FormattedText TextLayout(string text,Brush? brush=null,double size=11)=>TextDrawing(text,brush,size).Layout;
    public static void Label(DrawingContext dc,string text,double x,double y,Brush? b=null,double size=11)
    {
        var drawing=TextDrawing(text,b,size).Drawing;
        dc.PushTransform(new TranslateTransform(x,y));dc.DrawDrawing(drawing);dc.Pop();
    }
    public static void Line(DrawingContext dc,Brush b,double w,IEnumerable<Point> points)
    {var a=points.ToArray();if(a.Length<2)return;var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(a[0],false,false);c.PolyLineTo(a.Skip(1).ToArray(),true,false);}g.Freeze();dc.DrawGeometry(null,new Pen(b,w),g);}
}
public sealed class CurveEditor:FrameworkElement
{
    public List<Knot>? Points {get;set;}
    public bool LogX {get;set;}=true;
    public bool LogY {get;set;}
    public double MinX {get;set;}=20;
    public double MaxX {get;set;}=20000;
    public double MinY {get;set;}=-30;
    public double MaxY {get;set;}=12;
    public string Unit {get;set;}="dB";
    public string XUnit {get;set;}="Hz";
    public bool Editable {get;set;}=true;
    public bool FullEnvelope {get;set;}
    public double? LockedMinX {get;set;}
    public double? LockedMaxX {get;set;}
    public string LockedLabel {get;set;}="";
    bool IsLocked(double x)=>LockedMinX is double lo&&LockedMaxX is double hi&&(x>lo&&x<hi||lo<=MinX&&x<=lo||hi>=MaxX&&x>=hi);
    bool FixedEndpoint(double x)=>LockedMinX is double lo&&LockedMaxX is double hi&&(x==MinX||x==MaxX||x==lo||x==hi);
    internal double ValueAt(double x)
    {
        if(Points==null||IsLocked(x))return double.NaN;
        var points=LockedMinX is double lo&&LockedMaxX is double hi
            ?Points.Where(k=>x<=lo?k.X<=lo:k.X>=hi).ToList():Points;
        return points.Count==0?double.NaN:Curves.At(points,x,LogX,LogY);
    }
    public bool CanEditAt(double x)=>Editable&&!IsLocked(x);
    public double? Marker {get;set;}
    public Func<double,double> DisplayX {get;set;}=x=>x;
    public Func<double,double> ModelX {get;set;}=x=>x;
    public event Action? Changed;
    int drag=-1;bool changed;
    int sampleHash;Point[][]? sampled;
    internal int SampleBuildCount {get;private set;}
    Point[][] Samples()
    {
        var hash=new HashCode();hash.Add(MinX);hash.Add(MaxX);hash.Add(LogX);hash.Add(LogY);hash.Add(LockedMinX);hash.Add(LockedMaxX);
        // Envelope coordinate mappings can change without changing knot coordinates.
        for(int i=0;i<3;i++)hash.Add(ModelX(Value(i/2.0,MinX,MaxX,LogX)));
        foreach(var knot in Points!){hash.Add(knot.X);hash.Add(knot.Y);}
        int next=hash.ToHashCode();if(sampled!=null&&sampleHash==next)return sampled;
        sampleHash=next;SampleBuildCount++;
        Point[] Segment(double from,double to)=>Enumerable.Range(0,400).Select(i=>{double x=ModelX(i==0?from:i==399?to:Value(i/399.0,from,to,LogX));return new Point(x,ValueAt(x));}).ToArray();
        if(LockedMinX is double lo&&LockedMaxX is double hi)
        {
            var segments=new List<Point[]>();if(lo>MinX)segments.Add(Segment(MinX,lo));if(hi<MaxX)segments.Add(Segment(hi,MaxX));return sampled=segments.ToArray();
        }
        return sampled=[Segment(MinX,MaxX)];
    }
    Rect Area=>new(44,12,Math.Max(10,ActualWidth-60),Math.Max(10,ActualHeight-44));
    static double Fraction(double x,double min,double max,bool log)=>log?Math.Log(x/min)/Math.Log(max/min):(x-min)/(max-min);
    static double Value(double q,double min,double max,bool log)=>log?min*Math.Pow(max/min,q):min+(max-min)*q;
    Point Screen(Knot k)=>new(Area.Left+Area.Width*Fraction(DisplayX(k.X),MinX,MaxX,LogX),Area.Bottom-Area.Height*Fraction(k.Y,MinY,MaxY,LogY));
    Knot Data(Point p)=>new(ModelX(Value(Math.Clamp((p.X-Area.Left)/Area.Width,0,1),MinX,MaxX,LogX)),Value(Math.Clamp((Area.Bottom-p.Y)/Area.Height,0,1),MinY,MaxY,LogY));
    protected override void OnRender(DrawingContext dc)
    {
        Paint.PixelsPerDip=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRoundedRectangle(Paint.Paper,null,new(0,0,ActualWidth,ActualHeight),5,5);var r=Area;
        for(int i=0;i<=4;i++){double y=r.Top+r.Height*i/4;dc.DrawLine(new Pen(Paint.Grid,1),new(r.Left,y),new(r.Right,y));double v=Value(1-i/4.0,MinY,MaxY,LogY);Paint.Label(dc,v.ToString(Math.Abs(v)<.1?"0.###":"0.##"),2,y-6,Paint.Muted,10);}
        foreach(double f in LogX?new[]{20.0,80,1000,8000,20000}:Enumerable.Range(0,5).Select(i=>MinX+(MaxX-MinX)*i/4))
        {double x=r.Left+r.Width*Fraction(f,MinX,MaxX,LogX);dc.DrawLine(new Pen(Paint.Grid,1),new(x,r.Top),new(x,r.Bottom));Paint.Label(dc,f>=1000?$"{f/1000:0.#}k":f.ToString("0.##"),x-9,r.Bottom+5,Paint.Muted,10);}
        Paint.Label(dc,XUnit,r.Left,r.Bottom+19,Paint.Muted,10);
        if(Points is not {Count:>=2})return;
        if(Points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||LogX&&p.X<=0||LogY&&p.Y<=0)||Points.Zip(Points.Skip(1)).Any(p=>p.First.X>=p.Second.X)){Paint.Label(dc,SoundstageIR.Core.TextCatalog.T("T4374175386"),50,50,Paint.Muted);return;}
        dc.PushClip(new RectangleGeometry(r));
        foreach(var segment in Samples())Paint.Line(dc,Editable?Paint.Accent:Paint.Muted,2,segment.Select(p=>Screen(new(p.X,p.Y))));
        if(Marker is double marker && marker>=MinX&&marker<=MaxX)
        {double x=r.Left+r.Width*Fraction(marker,MinX,MaxX,LogX);dc.DrawLine(new Pen(Paint.Muted,1){DashStyle=DashStyles.Dash},new(x,r.Top),new(x,r.Bottom));Paint.Label(dc,SoundstageIR.Core.TextCatalog.T("TAD36B620F8"),Math.Min(x+4,r.Right-55),r.Top+5,Paint.Muted,10);}
        if(LockedMinX is double lockedLow&&LockedMaxX is double lockedHigh)
        {
            double a=r.Left+r.Width*Fraction(Math.Clamp(lockedLow,MinX,MaxX),MinX,MaxX,LogX);
            double b=r.Left+r.Width*Fraction(Math.Clamp(lockedHigh,MinX,MaxX),MinX,MaxX,LogX);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(218,216,208)),null,new(a,r.Top,Math.Max(0,b-a),r.Height));
            dc.DrawLine(new Pen(Paint.Muted,1){DashStyle=DashStyles.Dash},new(a,r.Top),new(a,r.Bottom));
            dc.DrawLine(new Pen(Paint.Muted,1){DashStyle=DashStyles.Dash},new(b,r.Top),new(b,r.Bottom));
            if(b-a>70)Paint.Label(dc,LockedLabel,a+5,r.Top+8,Paint.Muted,10);
        }
        foreach(var point in Points.Where(p=>!IsLocked(p.X))){Point pt=Screen(point);dc.DrawEllipse(Paint.Accent,new Pen(Paint.Paper,2),pt,5,5);}
        dc.Pop();Paint.Label(dc,Unit,r.Right-20,0,Paint.Muted,10);
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if(!Editable||Points is not {Count:>1})return;var p=e.GetPosition(this);
        int nearest=Enumerable.Range(0,Points.Count).Where(i=>!IsLocked(Points[i].X)).OrderBy(i=>(Screen(Points[i])-p).LengthSquared).DefaultIfEmpty(-1).First();
        bool hit=nearest>=0&&(Screen(Points[nearest])-p).Length<14;
        if(!CanEditAt(hit?Points[nearest].X:Data(p).X))return;
        if(e.ChangedButton==MouseButton.Right){if(hit&&!FixedEndpoint(Points[nearest].X)&&Points.Count>2&&(LogX||nearest>0&&nearest<Points.Count-1&&(!FullEnvelope||Points[nearest].X!=0))){Points.RemoveAt(nearest);Changed?.Invoke();InvalidateVisual();}return;}
        if(e.ClickCount==2&&!hit&&Area.Contains(p)&&Points.Count<EditingLimits.MaxKnots){var point=Data(p);if(Points.Any(k=>Math.Abs(k.X-point.X)<1e-6))return;Points.Add(point);Points.Sort((a,b)=>a.X.CompareTo(b.X));Changed?.Invoke();InvalidateVisual();return;}
        if(hit){drag=nearest;changed=false;CaptureMouse();e.Handled=true;}
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(Points==null)return;var pos=e.GetPosition(this);var q=Data(pos);ToolTip=$"{DisplayX(q.X):0.###} {XUnit} / {q.Y:0.###} {Unit}";
        if(drag<0||e.LeftButton!=MouseButtonState.Pressed)return;
        double oldX=Points[drag].X;
        double low=drag>0?Points[drag-1].X+1e-7:ModelX(MinX),high=drag<Points.Count-1?Points[drag+1].X-1e-7:ModelX(MaxX);
        if(LockedMinX is double lockedLow&&LockedMaxX is double lockedHigh)
        {if(FixedEndpoint(oldX))low=high=oldX;else if(oldX<lockedLow)high=Math.Min(high,Math.BitDecrement(lockedLow));else if(oldX>lockedHigh)low=Math.Max(low,Math.BitIncrement(lockedHigh));else return;}
        if(low>high)return;q.X=Math.Clamp(q.X,low,high);
        if(!LogX)
        {
            if(drag==0){q.X=oldX;if(!FullEnvelope)q.Y=0;}
            else if(drag==Points.Count-1){q.X=1;q.Y=-60;}
            else if(FullEnvelope){if(oldX==0)q.X=0;if(drag==Points.Count-2)q.Y=Math.Max(-59.9,q.Y);}
            else q.Y=Math.Clamp(q.Y,Points[drag+1].Y,Points[drag-1].Y);
        }
        Points[drag].X=q.X;Points[drag].Y=q.Y;changed=true;InvalidateVisual();
    }
    protected override void OnMouseUp(MouseButtonEventArgs e){if(drag<0)return;drag=-1;ReleaseMouseCapture();if(changed)Changed?.Invoke();}
}
public sealed class DirectionView:FrameworkElement
{
    public Project? Project {get;set;}
    public Guid? Selected {get;set;}
    public event Action<Guid>? SelectSource;
    double yaw=0,pitch=.9;Point mouse;bool rotating;
    readonly List<(Point P,Guid Id,string Label)> hits=[];
    public void ResetView()=>SetView(0,.9);
    public void SetView(double azimuth,double inclination){yaw=azimuth;pitch=Math.Clamp(inclination,-1.4,1.4);InvalidateVisual();}
    public Point EarMarker(bool left)=>ProjectPoint(left?-.23:.23,0,0);
    Point ProjectPoint(double x,double y,double z)
    {
        double u=x*Math.Cos(yaw)+z*Math.Sin(yaw),v=-x*Math.Sin(yaw)+z*Math.Cos(yaw);
        double sy=y*Math.Cos(pitch)-v*Math.Sin(pitch);double scale=Math.Min(ActualWidth,ActualHeight)*.34;
        return new(ActualWidth*.5+u*scale,ActualHeight*.49-sy*scale);
    }
    Point Direction(double az,double el,double radius=1)
    {double a=az*Math.PI/180,e=el*Math.PI/180;return ProjectPoint(Math.Sin(a)*Math.Cos(e)*radius,Math.Sin(e)*radius,Math.Cos(a)*Math.Cos(e)*radius);}
    protected override void OnRender(DrawingContext dc)
    {
        Paint.PixelsPerDip=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(Paint.Paper,null,new(0,0,ActualWidth,ActualHeight));hits.Clear();
        for(int plane=0;plane<3;plane++){int k=plane;Paint.Line(dc,Paint.Grid,1,Enumerable.Range(0,121).Select(i=>{double a=i*Math.PI/60;return k==0?ProjectPoint(Math.Cos(a),0,Math.Sin(a)):k==1?ProjectPoint(Math.Cos(a),Math.Sin(a),0):ProjectPoint(0,Math.Cos(a),Math.Sin(a));}));}
        Point center=ProjectPoint(0,0,0),front=Direction(0,0,1.17);dc.DrawLine(new Pen(Paint.Grid,1),center,front);Paint.Label(dc,SoundstageIR.Core.TextCatalog.T("T6044E9F095"),front.X-6,front.Y-12,Paint.Muted);
        if(Project!=null)
        {
            foreach(var s in Project.Sources){Dot(s,s.Azimuth,false);if(!s.Median)Dot(s,-s.Azimuth,true);}
            if(Project.Direct.Enabled)foreach(int side in new[]{-1,1}){var p=Direction(side*Project.Direct.Angle,Project.Direct.Elevation);dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(171,105,22)),null,new(p.X-6,p.Y-6,12,12));Paint.Label(dc,side<0?SoundstageIR.Core.TextCatalog.T("T9C0E5041CA"):SoundstageIR.Core.TextCatalog.T("TE1563CA0FE"),p.X+(side<0?-52:10),p.Y+12,new SolidColorBrush(Color.FromRgb(171,105,22)));}
        }
        DrawHead(dc,center);
        Paint.Label(dc,SoundstageIR.Core.TextCatalog.T("T790FB514BD"),12,ActualHeight-23,Paint.Muted,11);
        void Dot(ReflectionPair s,double az,bool mirror)
        {
            var p=Direction(az,s.Elevation);bool selected=s.Id==Selected;Brush b=!s.Enabled?Paint.Grid:mirror?new SolidColorBrush(Color.FromRgb(98,152,226)):Paint.Accent;
            dc.DrawLine(new Pen(Paint.Grid,1),center,p);dc.DrawEllipse(b,selected?new Pen(Paint.Text,2):null,p,selected?7:5,selected?7:5);
            if(selected)Paint.Label(dc,mirror?SoundstageIR.Core.TextCatalog.T("T176C09844E"):s.Name,p.X+(mirror?10:-s.Name.Length*11-10),p.Y-25,b,11);
            hits.Add((p,s.Id,$"{s.Name}{(mirror?SoundstageIR.Core.TextCatalog.T("T3506EAB72D"):"")}  {az:F1}° / {s.Elevation:F1}°"));
        }
    }
    void DrawHead(DrawingContext dc,Point center)
    {
        double radius=Math.Min(ActualWidth,ActualHeight)*.34*.2;
        var fill=new RadialGradientBrush(Color.FromRgb(245,246,248),Color.FromRgb(180,192,205));
        dc.DrawEllipse(fill,new Pen(Paint.Muted,1),center,radius,radius);
        for(int plane=0;plane<3;plane++)
        {
            int k=plane;
            Paint.Line(dc,new SolidColorBrush(Color.FromRgb(80,112,130)),.7,Enumerable.Range(0,97).Select(i=>
            {double a=i*Math.PI/48,c=.2*Math.Cos(a),s=.2*Math.Sin(a);return k==0?ProjectPoint(c,0,s):k==1?ProjectPoint(c,s,0):ProjectPoint(0,c,s);}));
        }
        foreach(bool left in new[]{true,false})
        {
            var at=EarMarker(left);Brush color=left?Paint.Accent:new SolidColorBrush(Color.FromRgb(98,152,226));
            dc.DrawEllipse(color,new Pen(Paint.Paper,1),at,4,4);
            var outward=ProjectPoint(left?-.38:.38,0,0);Paint.Label(dc,left?"L":"R",outward.X-5,outward.Y-8,color,13);
        }
        var front=Direction(0,0,.27);dc.DrawLine(new Pen(Paint.Accent,2),center,front);dc.DrawEllipse(Paint.Accent,null,front,3,3);
    }
    protected override void OnMouseDown(MouseButtonEventArgs e){mouse=e.GetPosition(this);var hit=hits.Where(h=>(h.P-mouse).Length<12).OrderBy(h=>(h.P-mouse).Length).ToList();if(hit.Count>0)SelectSource?.Invoke(hit[0].Id);else{rotating=true;CaptureMouse();}}
    protected override void OnMouseMove(MouseEventArgs e){var p=e.GetPosition(this);if(rotating){yaw+=(p.X-mouse.X)*.008;pitch=Math.Clamp(pitch+(p.Y-mouse.Y)*.008,-1.4,1.4);mouse=p;InvalidateVisual();}else ToolTip=hits.FirstOrDefault(h=>(h.P-p).Length<12).Label;}
    protected override void OnMouseUp(MouseButtonEventArgs e){rotating=false;ReleaseMouseCapture();}
}
public sealed record PlotLine(string Name,double[] X,double[] Y,string Color);
public sealed record PlotData(string Title,string XUnit,string YUnit,bool LogX,List<PlotLine> Lines,double? MinY=null,double? MaxY=null);
public sealed class PlotView:FrameworkElement
{
    PlotData? data;
    string? dataKey;
    public PlotData? Data
    {
        get=>data;
        set
        {
            if(ReferenceEquals(data,value))return;
            string? key=value==null?null:$"{TextCatalog.Identity(value.Title)}|{value.XUnit}|{TextCatalog.Identity(value.YUnit)}|{value.LogX}";
            if(key!=dataKey){dataKey=key;viewport=null;hidden.Clear();}
            data=value;lineRanges.Clear();drawings.Clear();
            if(value!=null)foreach(var line in value.Lines)
            {
                lineRanges[line]=Range(line,value.LogX);
                drawings[line]=new CurveDrawing(line,value.LogX);GeometryBuildCount++;
            }
            InvalidateVisual();
        }
    }
    readonly Dictionary<PlotLine,PlotBounds> lineRanges=[];
    readonly Dictionary<PlotLine,CurveDrawing> drawings=[];
    internal int GeometryBuildCount {get;private set;}
    internal int DrawCount {get;private set;}
    // Samples are immutable for a PlotData instance. Panning changes this geometry's
    // coordinate transform, not its points. Geometry.Transform keeps the pen width
    // in screen units, unlike applying a transform to the drawing context.
    sealed class CurveDrawing
    {
        public Brush Brush {get;}
        readonly Pen pen;
        readonly GeometryGroup geometry=new();
        readonly MatrixTransform transform=new();
        public CurveDrawing(PlotLine line,bool log)
        {
            Brush=LineBrush(line.Color);Brush.Freeze();pen=new(Brush,1.3);pen.Freeze();
            var path=new StreamGeometry();bool started=false;
            using(var context=path.Open())for(int i=0;i<Math.Min(line.X.Length,line.Y.Length);i++)
            {
                double x=line.X[i],y=line.Y[i];
                if(!double.IsFinite(x)||!double.IsFinite(y)||log&&x<=0){started=false;continue;}
                var p=new Point(log?Math.Log(x):x,y);
                if(!started){context.BeginFigure(p,false,false);started=true;}else context.LineTo(p,true,false);
            }
            path.Freeze();geometry.Children.Add(path);geometry.Transform=transform;
        }
        public void Draw(DrawingContext dc,Matrix matrix)
        {
            if(transform.Matrix!=matrix)transform.Matrix=matrix;
            dc.DrawGeometry(null,pen,geometry);
        }
    }
    static PlotBounds Range(PlotLine line,bool log)
    {
        double xmin=double.PositiveInfinity,xmax=double.NegativeInfinity,ymin=double.PositiveInfinity,ymax=double.NegativeInfinity;
        for(int i=0;i<Math.Min(line.X.Length,line.Y.Length);i++)
        {
            double x=line.X[i],y=line.Y[i];if(!double.IsFinite(x)||!double.IsFinite(y)||log&&x<=0)continue;
            xmin=Math.Min(xmin,x);xmax=Math.Max(xmax,x);ymin=Math.Min(ymin,y);ymax=Math.Max(ymax,y);
        }
        return new(xmin,xmax,ymin,ymax);
    }
    readonly HashSet<string> hidden=[];
    readonly List<(Rect Bounds,string Name)> legend=[];
    Rect area;
    PlotBounds? viewport;
    PlotBounds bounds,fullBounds;
    Point panStart;
    PlotBounds panBounds;
    bool panning;
    public bool IsZoomed=>viewport!=null;
    public bool IsLineVisible(string name)=>!hidden.Contains(TextCatalog.Identity(name));
    public void ToggleLine(string name){name=TextCatalog.Identity(name);if(!hidden.Add(name))hidden.Remove(name);InvalidateVisual();}
    public void ShowAll(){if(Data!=null)foreach(var line in Data.Lines)hidden.Remove(TextCatalog.Identity(line.Name));InvalidateVisual();}
    public void ShowOnly(string name){if(Data!=null)foreach(var line in Data.Lines){if(line.Name==name)hidden.Remove(TextCatalog.Identity(line.Name));else hidden.Add(TextCatalog.Identity(line.Name));}InvalidateVisual();}
    public void ResetView(){viewport=null;InvalidateVisual();}
    public void Zoom(double factor,double xFraction=.5,double yFraction=.5)
    {
        if(Data==null||bounds.XMax<=bounds.XMin||bounds.YMax<=bounds.YMin)return;
        viewport=bounds.Zoom(factor,xFraction,yFraction).Constrain(fullBounds);InvalidateVisual();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var point=e.GetPosition(this);if(Data==null||!area.Contains(point))return;
        Zoom(e.Delta>0?.8:1.25,(point.X-area.Left)/area.Width,(area.Bottom-point.Y)/area.Height);e.Handled=true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        var point=e.GetPosition(this);var hit=legend.FirstOrDefault(l=>l.Bounds.Contains(point));
        if(hit.Name!=null)
        {
            if(e.ChangedButton==MouseButton.Left){if(e.ClickCount==2)ShowOnly(hit.Name);else ToggleLine(hit.Name);e.Handled=true;}
            else if(e.ChangedButton==MouseButton.Right)
            {
                var menu=new System.Windows.Controls.ContextMenu();
                var solo=new System.Windows.Controls.MenuItem{Header=SoundstageIR.Core.TextCatalog.T("T059421366A")+hit.Name};solo.Click+=(_,_)=>ShowOnly(hit.Name);menu.Items.Add(solo);
                var all=new System.Windows.Controls.MenuItem{Header=SoundstageIR.Core.TextCatalog.T("TB1288E4AC0")};all.Click+=(_,_)=>ShowAll();menu.Items.Add(all);menu.IsOpen=true;e.Handled=true;
            }
            return;
        }
        if(e.ChangedButton!=MouseButton.Left||!area.Contains(point)||Data==null)return;
        if(e.ClickCount==2){ResetView();e.Handled=true;return;}
        panning=true;panStart=point;panBounds=bounds;CaptureMouse();Cursor=Cursors.ScrollAll;e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point=e.GetPosition(this);
        if(panning)
        {
            double dx=(point.X-panStart.X)/area.Width*(panBounds.XMax-panBounds.XMin);
            double dy=(point.Y-panStart.Y)/area.Height*(panBounds.YMax-panBounds.YMin);
            viewport=panBounds.Pan(-dx,dy).Constrain(fullBounds);InvalidateVisual();return;
        }
        if(Data!=null&&area.Contains(point))
        {
            double x=bounds.XMin+(point.X-area.Left)/area.Width*(bounds.XMax-bounds.XMin);
            if(Data.LogX)x=Math.Exp(x);
            double y=bounds.YMin+(area.Bottom-point.Y)/area.Height*(bounds.YMax-bounds.YMin);
            ToolTip=$"{x:0.###} {Data.XUnit} · {y:0.###} {Data.YUnit}";
        }
        else ToolTip=SoundstageIR.Core.TextCatalog.T("T757127C5A0");
    }
    protected override void OnMouseUp(MouseButtonEventArgs e){if(panning){panning=false;ReleaseMouseCapture();Cursor=Cursors.Arrow;}}
    protected override void OnLostMouseCapture(MouseEventArgs e){panning=false;Cursor=Cursors.Arrow;}
    protected override void OnRender(DrawingContext dc)
    {
        DrawCount++;
        Paint.PixelsPerDip=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        legend.Clear();dc.DrawRectangle(Paint.Paper,null,new(0,0,ActualWidth,ActualHeight));
        if(Data==null){Paint.Label(dc,SoundstageIR.Core.TextCatalog.T("T74C2DABE30"),20,25,Paint.Muted,14);return;}
        var d=Data;Paint.Label(dc,d.Title,12,8,Paint.Text,12);
        var all=d.Lines.Where(l=>l.X.Length>0&&l.Y.Length==l.X.Length).ToArray();if(all.Length==0)return;
        double lx=55,ly=0;var layout=new List<(PlotLine Line,Rect Rect)>();
        foreach(var line in all)
        {
            var label=Paint.TextLayout(line.Name);
            double width=label.Width+34;if(lx+width>ActualWidth-10&&lx>55){lx=55;ly+=23;}layout.Add((line,new(lx,ly,width,22)));lx+=width;
        }
        double legendTop=ActualHeight-ly-27;
        foreach(var item in layout)
        {
            var rect=item.Rect;rect.Y+=legendTop;legend.Add((rect,item.Line.Name));bool visible=IsLineVisible(item.Line.Name);
            var color=visible?drawings[item.Line].Brush:Paint.Muted;
            dc.DrawRectangle(visible?color:null,new Pen(color,1),new(rect.X,rect.Y+6,10,10));Paint.Label(dc,item.Line.Name,rect.X+17,rect.Y+3,color,11);
        }
        area=new(55,40,Math.Max(10,ActualWidth-78),Math.Max(10,legendTop-76));var r=area;
        var valid=all.Where(l=>IsLineVisible(l.Name)).ToArray();var scale=valid.Length>0?valid:all;
        var ranges=scale.Select(l=>lineRanges[l]).Where(r=>double.IsFinite(r.XMin)).ToArray();if(ranges.Length==0)return;
        double xmin=ranges.Min(r=>r.XMin),xmax=ranges.Max(r=>r.XMax);if(d.LogX){xmin=Math.Log(xmin);xmax=Math.Log(xmax);}if(xmax<=xmin)xmax=xmin+1;
        double ymin=d.MinY??ranges.Min(r=>r.YMin),ymax=d.MaxY??ranges.Max(r=>r.YMax);
        // The requested magnitude ceiling is a default, not a clipping limit.
        // Cached visible-line extrema keep legend changes and resize free of FFT work.
        if(d.XUnit=="Hz"&&d.YUnit=="dB")
            ymax=Math.Max(ymax,Math.Ceiling((ranges.Max(r=>r.YMax)+3)/10)*10);
        if(ymax-ymin<1e-8){ymin-=1;ymax+=1;}
        fullBounds=new(xmin,xmax,ymin,ymax);bounds=viewport?.Constrain(fullBounds)??fullBounds;
        double X(double x)=>r.Left+r.Width*((d.LogX?Math.Log(x):x)-bounds.XMin)/(bounds.XMax-bounds.XMin);
        double Y(double y)=>r.Bottom-r.Height*(y-bounds.YMin)/(bounds.YMax-bounds.YMin);
        for(int i=0;i<=4;i++)
        {
            double v=bounds.YMin+(bounds.YMax-bounds.YMin)*i/4;double y=Y(v);
            dc.DrawLine(new Pen(Paint.Grid,1),new(r.Left,y),new(r.Right,y));Paint.Label(dc,v.ToString("0.##"),3,y-6,Paint.Muted,10);
        }
        IEnumerable<double> ticks;
        if(d.LogX)
        {
            var candidates=new List<double>();
            for(int decade=(int)Math.Floor(bounds.XMin/Math.Log(10))-1;decade<=(int)Math.Ceiling(bounds.XMax/Math.Log(10));decade++)
                foreach(double multiplier in new[]{1d,2,5}){double value=multiplier*Math.Pow(10,decade);if(Math.Log(value)>=bounds.XMin-1e-10&&Math.Log(value)<=bounds.XMax+1e-10)candidates.Add(value);}
            double last=double.NegativeInfinity;var selected=new List<double>();
            foreach(double value in candidates)if(X(value)-last>=48){selected.Add(value);last=X(value);}
            ticks=selected.Count>1?selected:Enumerable.Range(0,4).Select(i=>Math.Exp(bounds.XMin+(bounds.XMax-bounds.XMin)*i/3));
        }
        else ticks=Enumerable.Range(0,5).Select(i=>bounds.XMin+(bounds.XMax-bounds.XMin)*i/4);
        foreach(double f in ticks)
        {
            double x=X(f);dc.DrawLine(new Pen(Paint.Grid,1),new(x,r.Top),new(x,r.Bottom));
            Paint.Label(dc,Math.Abs(f)>=1000?$"{f/1000:0.##}k":f.ToString("0.##"),x-12,r.Bottom+5,Paint.Muted,10);
        }
        double sx=r.Width/(bounds.XMax-bounds.XMin),sy=-r.Height/(bounds.YMax-bounds.YMin);
        var matrix=new Matrix(sx,0,0,sy,r.Left-bounds.XMin*sx,r.Bottom-bounds.YMin*sy);
        dc.PushClip(new RectangleGeometry(r));foreach(var line in valid)drawings[line].Draw(dc,matrix);dc.Pop();
        if(valid.Length==0)Paint.Label(dc,SoundstageIR.Core.TextCatalog.T("T7096D722A3"),r.Left+15,r.Top+15,Paint.Muted);
        Paint.Label(dc,d.YUnit,r.Left,25,Paint.Muted,10);Paint.Label(dc,d.XUnit,r.Right-20,r.Bottom+20,Paint.Muted,10);
    }
    static Brush LineBrush(string source)
    {
        var c=(Color)ColorConverter.ConvertFromString(source);double brightness=(c.R*.2126+c.G*.7152+c.B*.0722)/255;
        if(brightness>.58){double k=.5/brightness;c=Color.FromRgb((byte)(c.R*k),(byte)(c.G*k),(byte)(c.B*k));}
        return new SolidColorBrush(c);
    }
}

public readonly record struct PlotBounds(double XMin,double XMax,double YMin,double YMax)
{
    public PlotBounds Zoom(double factor,double xFraction,double yFraction)
    {
        factor=Math.Clamp(factor,.1,10);
        double x=XMin+(XMax-XMin)*xFraction,y=YMin+(YMax-YMin)*yFraction;
        double w=Math.Clamp((XMax-XMin)*factor,1e-8,1e9),h=Math.Clamp((YMax-YMin)*factor,1e-8,1e9);
        return new(x-w*xFraction,x+w*(1-xFraction),y-h*yFraction,y+h*(1-yFraction));
    }
    public PlotBounds Constrain(PlotBounds full)
    {
        double w=Math.Min(XMax-XMin,full.XMax-full.XMin),h=Math.Min(YMax-YMin,full.YMax-full.YMin);
        double x=Math.Clamp(XMin,full.XMin,Math.Max(full.XMin,full.XMax-w)),y=Math.Clamp(YMin,full.YMin,Math.Max(full.YMin,full.YMax-h));
        return new(x,x+w,y,y+h);
    }
    public PlotBounds Pan(double dx,double dy)=>new(XMin+dx,XMax+dx,YMin+dy,YMax+dy);
}
