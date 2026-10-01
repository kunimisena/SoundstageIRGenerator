namespace SoundstageIR.Core.Speakers;

// Each branch stores only its own frequency interval. There is no middle curve.
public sealed class OutsideReverb
{
    public Excitation Low {get;set;}=new();
    public Excitation High {get;set;}=new();
    public static OutsideReverb Create(Project field,double low,double high)
    {
        var sources=field.Sources.Where(s=>s.Enabled).ToArray();
        var e=sources.Length==0?new Excitation():new ReflectionEditSession(field,sources.Select(s=>s.Id)).Reference.Clone();
        e.Energy=e.Energy.Select(k=>new Knot(k.X,Math.Clamp(e.EnergyDb(k.X),-120,60))).ToList();
        e.GainDb=e.TiltDbPerOct=0;
        var result=new OutsideReverb{Low=e.Clone(),High=e.Clone()};result.ResizeLow(low);result.ResizeHigh(high);return result;
    }
    public void ResizeLow(double edge)=>Resize(Low,20,edge);
    public void ResizeHigh(double edge)=>Resize(High,edge,20000);
    static void Resize(Excitation e,double from,double to)
    {e.Energy=Slice(e.Energy,from,to,false);e.Decay=Slice(e.Decay,from,to,true);}
    static List<Knot> Slice(List<Knot> points,double from,double to,bool logY)
    {
        // Expanding a branch extends its own endpoint; shrinking samples only that branch.
        return points.Where(k=>k.X>from&&k.X<to).Select(k=>k.X).Concat([from,to]).Distinct().Order()
            .Select(x=>new Knot(x,Curves.At(points,x,true,logY))).ToList();
    }
    public void Validate(double low,double high)
    {
        void Branch(Excitation e,double from,double to)
        {
            foreach(var curve in new[]{e.Energy,e.Decay})
                if(curve.Count==0||curve[0].X!=from||curve[^1].X!=to||curve.Any(k=>k.X<from||k.X>to))
                    throw new ArgumentException("Outside curve exceeds its branch / 带外曲线超出所属频段");
            // A disabled branch retains one endpoint for reopening the band.
            if(from==to){var copy=e.Clone();copy.Energy=[new(20,e.Energy[0].Y),new(20000,e.Energy[0].Y)];copy.Decay=[new(20,e.Decay[0].Y),new(20000,e.Decay[0].Y)];copy.Validate();}
            else e.Validate();
        }
        Branch(Low,20,low);Branch(High,high,20000);
    }
}
