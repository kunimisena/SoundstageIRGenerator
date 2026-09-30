namespace SoundstageIR.Core;

/// <summary>Editing normalization only. DSP validation still rejects malformed structure.</summary>
public static class EditingLimits
{
    public const double MinRt=.005,MaxRt=30,MaxPath=3430;
    public const int MaxKnots=256;
    public static double Clamp(double v,double min,double max)=>double.IsNaN(v)?min:Math.Clamp(v,min,max);
    public static int Normalize(Excitation e)
    {
        int n=0;double C(double v,double a,double b){double r=Clamp(v,a,b);if(r!=v)n++;return r;}
        e.GainDb=C(e.GainDb,-120,60);e.TiltDbPerOct=C(e.TiltDbPerOct,-24,24);
        e.FirstReflectionExtraPath=C(e.FirstReflectionExtraPath,0,MaxPath);e.MixingPath=C(e.MixingPath,0,MaxPath);
        void FrequencyDomain(List<Knot> curve)
        {
            foreach(var k in curve)k.X=C(k.X,20,20000);
            if(curve.Count<2)return;
            if(curve.Zip(curve.Skip(1)).Any(v=>v.First.X>=v.Second.X))
            {
                var sorted=curve.GroupBy(k=>k.X).Select(g=>g.Last()).OrderBy(k=>k.X).ToList();
                if(sorted.Count==1){double x=sorted[0].X==20?20000:20;sorted.Add(new(x,sorted[0].Y));sorted.Sort((a,b)=>a.X.CompareTo(b.X));}
                curve.Clear();curve.AddRange(sorted);n++;
            }
        }
        FrequencyDomain(e.Energy);FrequencyDomain(e.Decay);
        foreach(var k in e.Energy)k.Y=C(k.Y,-120,60);
        foreach(var k in e.Decay)k.Y=C(k.Y,MinRt,MaxRt);
        if(e.Envelope is {Count: >=3} env)
        {
            foreach(var k in env)k.Y=C(k.Y,-120,24);
            env[^1].Y=C(env[^1].Y,-60,-60);if(env[^2].Y<=-60)env[^2].Y=C(env[^2].Y,-59.99,24);
        }
        return n;
    }
    public static int Normalize(Project p)
    {
        int n=0;double C(double v,double a,double b){double r=Clamp(v,a,b);if(r!=v)n++;return r;}
        p.EarEqStrengthPercent=C(p.EarEqStrengthPercent,0,100);p.CenterEqStrengthPercent=C(p.CenterEqStrengthPercent,0,100);
        p.ReflectionEnergyPercent=C(p.ReflectionEnergyPercent,0,100);p.Direct.AirAbsorptionDistance=C(p.Direct.AirAbsorptionDistance,0,10000);
        p.Direct.Angle=C(p.Direct.Angle,0,180);p.Direct.Elevation=C(p.Direct.Elevation,-90,90);
        p.OutputDb=C(p.OutputDb,-36,18);
        p.Smooth1=(int)C(p.Smooth1,1,24);p.Smooth2=(int)C(p.Smooth2,1,24);
        p.HeadRadius=C(p.HeadRadius,.05,.12);p.HeadShadow=C(p.HeadShadow,0,2);
        foreach(var s in p.Sources)
        {
            n+=Normalize(s.Left)+Normalize(s.Right);
            s.Azimuth=C(s.Azimuth,-180,0);s.Elevation=C(s.Elevation,-90,90);
            s.RightGainDb=C(s.RightGainDb,-120,60);s.RightTilt=C(s.RightTilt,-24,24);
        }
        return n;
    }
}
