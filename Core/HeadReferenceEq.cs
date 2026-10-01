using System.Numerics;
namespace SoundstageIR.Core;

public sealed record HeadReferenceInput(double[] LeftHead,double[] RightHead);
// Equal-energy unit impulses in each enabled direction. Only squared magnitudes
// enter the reference; propagation phase, random kernels and cross terms do not.
public sealed class HeadReferenceEq
{
    readonly double[] power;
    readonly int sr,n;
    public int FftLength=>n;
    public int DirectionCount {get;}
    HeadReferenceEq(double[] power,int sr,int n,int directions)
    {this.power=power;this.sr=sr;this.n=n;DirectionCount=directions;}
    public static HeadReferenceEq Build(IReadOnlyList<HeadReferenceInput> inputs,int n,int sr,CancellationToken ct=default)
    {
        if(inputs.Count==0)throw new ArgumentException("人头校正需要至少一个启用方向。");
        var power=new double[n/2+1];
        foreach(var input in inputs)foreach(var h in new[]{input.LeftHead,input.RightHead})
        {
            ct.ThrowIfCancellationRequested();var a=Dsp.Spectrum(h,n);
            for(int k=0;k<power.Length;k++)power[k]+=Dsp.Power(a[k])/(2*inputs.Count);
        }
        return new(power,sr,n,inputs.Count);
    }
    public double[] ReferencePower()=>(double[])power.Clone();
    public double[] GainDb()
    {
        var db=new double[power.Length];
        int lo=Math.Max(1,(int)Math.Ceiling(10.0*n/sr)),hi=Math.Min(n/2,(int)Math.Floor(Dsp.BandpassStopHigh(sr)*n/sr));
        for(int k=lo;k<=hi;k++)
        {
            if(power[k]<=0||!double.IsFinite(power[k]))throw new InvalidOperationException($"人头功率参考在 {k*(double)sr/n:F2} Hz 为零或无效，无法求逆。");
            db[k]=-10*Math.Log10(power[k]);
        }
        Array.Fill(db,db[lo],0,lo);Array.Fill(db,db[hi],hi+1,db.Length-hi-1);return db;
    }
    public EqSpectrum Plan(int support,CancellationToken ct=default)
    {
        var db=GainDb();var spectrum=EqDesigner.MinimumPhaseSpectrum(db,ct);
        int lo=(int)Math.Ceiling(20.0*n/sr),hi=(int)Math.Floor(20000.0*n/sr),max=lo,min=lo;double sum=0;
        for(int k=lo;k<=hi;k++){sum+=db[k];if(db[k]>db[max])max=k;if(db[k]<db[min])min=k;}
        double mean=sum/(hi-lo+1);
        return new(db,spectrum,new("人头 EQ（单位冲激·非相干功率）",db[max],db[min],db[max]-mean,db[min]-mean,max*(double)sr/n,min*(double)sr/n,support,0,0));
    }
    public static (double Az,double El)[] Directions(Project p)
    {
        var directions=new List<(double Az,double El)>();
        if(p.Direct.Enabled){directions.Add((-p.Direct.Angle,p.Direct.Elevation));directions.Add((p.Direct.Angle,p.Direct.Elevation));}
        foreach(var source in p.Sources.Where(s=>s.Enabled))
        {directions.Add((source.Azimuth,source.Elevation));if(!source.Median)directions.Add((-source.Azimuth,source.Elevation));}
        return directions.Select(v=>(Math.Abs(v.El)==90?0:Math.Abs(v.Az)==180?180:v.Az,v.El)).Distinct().OrderBy(v=>v.Item1).ThenBy(v=>v.El).ToArray();
    }
}
