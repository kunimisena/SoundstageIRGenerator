using SoundstageIR.Core;
public static class PresetRevisionChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        foreach(var preset in Presets.BuiltIn)
        {
            var p=preset.Create();
            check(p.HeadModel==HeadModelKind.Fabian&&p.FabianCtfCompensation&&!p.StrictMirror,"New template head defaults "+p.Name);
            foreach(var s in p.Sources)
            {
                check(s.Left.Energy.Zip(s.Left.Energy.Skip(1)).All(v=>v.First.Y>=v.Second.Y),"Broad lowpass energy profile "+p.Name+"/"+s.Name);
                check(s.Left.Rt(80)>=.94*s.Left.Rt(1000)&&s.Left.Rt(8000)<s.Left.Rt(1000),"No compulsory 1k RT peak "+p.Name+"/"+s.Name);
            }
        }
        var options=new PresetOptions{Rt=999,ExtraPath=-99,MixingPath=1e9,WetPercent=130,AirDistance=-4};
        var extreme=Presets.BuiltIn[^1].Create(options);extreme.Validate();
        check(options.Rt==30&&options.ExtraPath==0&&options.MixingPath==3430&&options.WetPercent==100&&options.AirDistance==0,"Extreme template inputs clamp and create successfully");
        var p0=Presets.BuiltIn[0].Create();p0.Sources[0].Left.Decay.ForEach(k=>k.Y=20);
        var session=new ReflectionEditSession(p0,p0.Sources.Select(s=>s.Id));
        session.Reference.Decay.ForEach(k=>k.Y*=4);session.Reference.FirstReflectionExtraPath=10000;
        var revised=session.Apply(p0);revised.Validate();
        check(session.ClampedValues>0&&revised.Sources.All(s=>s.Left.Decay.All(k=>k.Y<=30)),"Relative multi-source scaling clips saturated sources without refusing other changes");
        check(revised.Sources[1].Left.Rt(1000)>p0.Sources[1].Left.Rt(1000),"Unsaturated sources still receive requested relative edit");
        var e=new Excitation{GainDb=double.PositiveInfinity,TiltDbPerOct=-999,FirstReflectionExtraPath=-1,MixingPath=99999};
        e.Decay[0].Y=double.NaN;e.Energy[0].Y=-999;EditingLimits.Normalize(e);e.Validate();
        check(e.GainDb==60&&e.TiltDbPerOct==-24&&e.Rt(80)==.005,"Nonfinite and extreme scalar edits normalize before synthesis");
        e.Energy=[new(-3,0),new(0,-1),new(99999,-8)];EditingLimits.Normalize(e);e.Validate();
        check(e.Energy.Count==2&&e.Energy[0].X==20&&e.Energy[^1].X==20000,"Out-of-range frequency points clamp, merge and remain editable");
        var validEdge=new Excitation{Envelope=[new(-1,0),new(0,-1),new(.99,-59.999),new(1,-60)]};
        string edgeBefore=ProjectIO.Serialize(validEdge);
        check(EditingLimits.Normalize(validEdge)==0&&ProjectIO.Serialize(validEdge)==edgeBefore,"Valid near-end envelope stays byte-identical");
        var many=Presets.BuiltIn[0].Create();
        for(int i=0;i<many.Sources.Count;i++)
            many.Sources[i].Left.Energy=Enumerable.Range(0,150).Select(j=>new Knot(20*Math.Pow(1000,(j+.05*i)/150.0),-3+Math.Sin(j*.18))).ToList();
        var fit=new ReflectionEditSession(many,many.Sources.Select(s=>s.Id));
        fit.Reference.Energy.ForEach(k=>k.Y-=2);var fitted=fit.Apply(many);fitted.Validate();
        check(fit.CurveSimplified&&fit.Reference.Energy.Count<=256&&fitted.Sources.All(s=>s.Left.Energy.Count<=256),"Complex multi-source curves yield bounded approximation instead of error");
        var longP=Presets.BuiltIn[0].Create();longP.Sources=longP.Sources.Take(1).ToList();
        foreach(var side in new[]{longP.Sources[0].Left,longP.Sources[0].Right})side.Decay.ForEach(k=>k.Y=12);
        longP.Equalize=false;
        var result=Generator.Generate(longP);
        check(result.Duration>16&&result.Kernels.All(h=>h.All(double.IsFinite)),"Extended 12-second RT actually generates finite kernels");
        var shortResult=Generator.Generate(Presets.BuiltIn[0].Create());
        check(result.ZeroSample==shortResult.ZeroSample,"Long RT does not add direct-path delay");
        File.WriteAllText(Path.Combine(folder,"preset-revision.json"),ProjectIO.Serialize(Presets.BuiltIn.Select(x=>x.Create()).ToArray()));
    }
}
