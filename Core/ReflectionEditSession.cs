namespace SoundstageIR.Core;

/// <summary>Atomic, relative edits to an explicit source set; no persistent DSP layer.</summary>
public sealed class ReflectionEditSession
{
    readonly string snapshot;
    readonly HashSet<Guid> ids;
    public Excitation Baseline {get;}
    public Excitation Reference {get;}
    public int Count=>ids.Count;
    public int ClampedValues {get;private set;}
    public bool CurveSimplified {get;private set;}
    public double AzimuthDelta {get;set;}
    public double ElevationDelta {get;set;}
    public double RightGainDelta {get;set;}
    public double RightTiltDelta {get;set;}
    // 0 = keep, 1 = on/automatic, 2 = off/manual.
    public int EnabledMode {get;set;}
    public int RightMode {get;set;}
    public ReflectionEditSession(Excitation kernel)
    {ids=[];snapshot="";Baseline=kernel.Clone();Baseline.Validate();Reference=Baseline.Clone();}
    public ReflectionEditSession(Project project,IEnumerable<Guid> targets)
    {
        ids=targets.ToHashSet();var sources=project.Sources.Where(s=>ids.Contains(s.Id)).ToArray();
        if(sources.Length==0||sources.Length!=ids.Count)throw new ArgumentException("请选择至少一个现有反射源。");
        snapshot=SourceSnapshot(project);var e=sources.Select(s=>s.Left).ToArray();
        var envelopes=e.Select(s=>{return s.Clone();}).ToArray();
        Baseline=new(){GainDb=e.Average(s=>s.GainDb),TiltDbPerOct=e.Average(s=>s.TiltDbPerOct),
            FirstReflectionExtraPath=(double)e.Average(s=>(decimal)s.FirstReflectionExtraPath),MixingPath=(double)e.Average(s=>(decimal)s.MixingPath),
            Energy=Fit(x=>e.Average(s=>Curves.At(s.Energy,x,true)),e.SelectMany(s=>s.Energy.Select(k=>k.X)),true,false),
            Decay=Fit(x=>Math.Exp(e.Average(s=>Math.Log(s.Rt(x)))),e.SelectMany(s=>s.Decay.Select(k=>k.X)),true,true),
            Envelope=Fit(x=>envelopes.Average(s=>Curves.At(s.Envelope!,x)),envelopes.SelectMany(s=>s.Envelope!.Select(k=>k.X)),false,false)};
        Baseline.Validate();Reference=Baseline.Clone();
    }
    string SourceSnapshot(Project p)=>ProjectIO.Serialize(p.Sources.Where(s=>ids.Contains(s.Id)).ToArray());
    public bool IsCurrent(Project project)=>snapshot==SourceSnapshot(project);
    public bool HasChanges=>Baseline.GainDb!=Reference.GainDb||Baseline.TiltDbPerOct!=Reference.TiltDbPerOct||Baseline.FirstReflectionExtraPath!=Reference.FirstReflectionExtraPath||Baseline.MixingPath!=Reference.MixingPath||!Equal(Baseline.Energy,Reference.Energy)||!Equal(Baseline.Decay,Reference.Decay)||!Equal(Baseline.Envelope,Reference.Envelope)||AzimuthDelta!=0||ElevationDelta!=0||RightGainDelta!=0||RightTiltDelta!=0||EnabledMode!=0||RightMode!=0;
    public Project Apply(Project current)
    {
        if(!IsCurrent(current))throw new InvalidOperationException("项目已变化，请重新读取调整范围后再应用。");
        ClampedValues=EditingLimits.Normalize(Reference);Reference.Validate();AzimuthDelta=Clip(AzimuthDelta,-180,180);ElevationDelta=Clip(ElevationDelta,-180,180);
        RightGainDelta=Clip(RightGainDelta,-120,120);RightTiltDelta=Clip(RightTiltDelta,-48,48);
        if(EnabledMode is <0 or >2||RightMode is <0 or >2)throw new ArgumentException("未知的批量状态。");
        var result=ProjectIO.Clone(current);
        bool energy=!Equal(Baseline.Energy,Reference.Energy),rt=!Equal(Baseline.Decay,Reference.Decay),env=!Equal(Baseline.Envelope!,Reference.Envelope!);
        foreach(var s in result.Sources.Where(s=>ids.Contains(s.Id)))
        {
            // Switching to manual captures the currently heard R excitation first.
            if(RightMode==2&&s.AutoRight)s.Right=s.EffectiveRight();
            if(RightMode!=0)s.AutoRight=RightMode==1;
            void Edit(Excitation e)
            {
                e.GainDb+=Reference.GainDb-Baseline.GainDb;e.TiltDbPerOct+=Reference.TiltDbPerOct-Baseline.TiltDbPerOct;
                e.FirstReflectionExtraPath=Scale(e.FirstReflectionExtraPath,Baseline.FirstReflectionExtraPath,Reference.FirstReflectionExtraPath);
                e.MixingPath=Scale(e.MixingPath,Baseline.MixingPath,Reference.MixingPath);
                if(energy){var old=e.Energy;e.Energy=Fit(f=>Clip(Curves.At(old,f,true)+Curves.At(Reference.Energy,f,true)-Curves.At(Baseline.Energy,f,true),-120,60),Xs(old,Baseline.Energy,Reference.Energy),true,false);}
                if(rt){var old=e.Decay;e.Decay=Fit(f=>Clip(Curves.At(old,f,true,true)*Reference.Rt(f)/Baseline.Rt(f),EditingLimits.MinRt,EditingLimits.MaxRt),Xs(old,Baseline.Decay,Reference.Decay),true,true);}
                if(env){var old=e.Envelope!;e.Envelope=Fit(u=>Clip(Curves.At(old,u)+Curves.At(Reference.Envelope!,u)-Curves.At(Baseline.Envelope!,u),-120,24),Xs(old,Baseline.Envelope!,Reference.Envelope!),false,false);}
            }
            Edit(s.Left);Edit(s.Right);
            s.RightGainDb+=RightGainDelta;s.RightTilt+=RightTiltDelta;
            if(!s.AutoRight){s.Right.GainDb+=RightGainDelta;s.Right.TiltDbPerOct+=RightTiltDelta;}
            s.Azimuth+=AzimuthDelta;s.Elevation+=ElevationDelta;
            if(EnabledMode!=0)s.Enabled=EnabledMode==1;
            ClampedValues+=EditingLimits.Normalize(s.Left)+EditingLimits.Normalize(s.Right);
        }
        ClampedValues+=EditingLimits.Normalize(result);result.Validate();return result;
    }
    double Clip(double v,double a,double b){double r=EditingLimits.Clamp(v,a,b);if(r!=v)ClampedValues++;return r;}
    static double Scale(double value,double baseline,double target)=>baseline==target?value:baseline>0?value*(target/baseline):value+target;
    static bool Equal(List<Knot> a,List<Knot> b)=>a.Count==b.Count&&a.Zip(b).All(v=>v.First.X==v.Second.X&&v.First.Y==v.Second.Y);
    static IEnumerable<double> Xs(params List<Knot>[] curves)=>curves.SelectMany(c=>c.Select(k=>k.X));
    // Adaptive bounded PCHIP fit. Report approximation at capacity; identity edits remain exact.
    List<Knot> Fit(Func<double,double> f,IEnumerable<double> positions,bool logX,bool logY)
    {
        var xs=positions.Distinct().Order().ToList();
        if(xs.Count>EditingLimits.MaxKnots)
        {
            var selected=Enumerable.Range(0,EditingLimits.MaxKnots-1).Select(i=>xs[(int)Math.Round(i*(xs.Count-1.0)/(EditingLimits.MaxKnots-2))]).ToList();
            if(xs.Contains(0))selected.Add(0);xs=selected.Distinct().Order().ToList();CurveSimplified=true;
        }
        var curve=xs.Select(x=>new Knot(x,f(x))).ToList();
        for(int pass=0;pass<EditingLimits.MaxKnots;pass++)
        {
            double worst=0,at=0;
            for(int i=0;i<curve.Count-1;i++)foreach(double t in new[]{.25,.5,.75})
            {
                double x=logX?curve[i].X*Math.Pow(curve[i+1].X/curve[i].X,t):curve[i].X+(curve[i+1].X-curve[i].X)*t;
                double expected=f(x),actual=Curves.At(curve,x,logX,logY);
                double error=logY?Math.Abs(Math.Log(actual/expected))/.002:Math.Abs(actual-expected)/.02;
                if(error>worst){worst=error;at=x;}
            }
            if(worst<=1)return curve;
            if(curve.Count>=EditingLimits.MaxKnots){CurveSimplified=true;return curve;}
            curve.Add(new(at,f(at)));curve.Sort((a,b)=>a.X.CompareTo(b.X));
        }
        CurveSimplified=true;return curve;
    }
}
