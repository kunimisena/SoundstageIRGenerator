using StatisticalField.Core;
public static class ReflectionEditChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        Directory.CreateDirectory(folder);
        foreach(var preset in Presets.BuiltIn.Where(p=>p.Directions>0))
        {
            var p=preset.Create();string original=ProjectIO.Serialize(p);var edit=new ReflectionEditSession(p,p.Sources.Select(s=>s.Id));
            check(ProjectIO.Serialize(edit.Apply(p))==original,"Identity shared edit keeps all parameters exact: "+preset.Name);
            foreach(var k in edit.Reference.Energy)k.Y-=3;
            foreach(var k in edit.Reference.Decay)k.Y*=.8;
            foreach(var k in edit.Reference.Envelope!)if(k.X<1)k.Y+=1.5*(1-k.X)/2;
            edit.Reference.FirstReflectionExtraPath*=.8;edit.Reference.MixingPath*=1.2;
            var changed=edit.Apply(p);changed.Validate();
            double energyError=0,rtError=0,envError=0;
            for(int i=0;i<p.Sources.Count;i++)
            {
                var old=p.Sources[i].Left;var now=changed.Sources[i].Left;
                foreach(double f in Enumerable.Range(0,301).Select(j=>20*Math.Pow(1000,j/300.0)))
                {
                    energyError=Math.Max(energyError,Math.Abs(now.EnergyDb(f)-old.EnergyDb(f)+3));
                    rtError=Math.Max(rtError,Math.Abs(now.Rt(f)/old.Rt(f)-.8));
                }
                foreach(double u in Enumerable.Range(0,301).Select(j=>-1+2*j/300.0))
                {
                    double expected=Curves.At(old.Envelope!,u)+Curves.At(edit.Reference.Envelope!,u)-Curves.At(edit.Baseline.Envelope!,u);
                    envError=Math.Max(envError,Math.Abs(Curves.At(now.Envelope!,u)-expected));
                }
                check(changed.Sources[i].Id==p.Sources[i].Id&&changed.Sources[i].Azimuth==p.Sources[i].Azimuth&&Math.Abs(now.FirstReflectionExtraPath-old.FirstReflectionExtraPath*.8)<1e-12,"Source identity/direction and path ratios preserved: "+preset.Name+"/"+i);
            }
            check(energyError<.025&&rtError<.002&&envError<.03,"Continuous relative curves preserved: "+preset.Name+" errors "+energyError+" / "+rtError+" / "+envError);
            check(ProjectIO.Serialize(p)==original,"Shared transform does not mutate original project: "+preset.Name);
            var reopened=ProjectIO.Deserialize(ProjectIO.Serialize(changed));check(ProjectIO.Serialize(reopened)==ProjectIO.Serialize(changed),"Adjusted curves persist: "+preset.Name);
        }
        var project=Presets.BuiltIn[3].Create();project.Sources[0].AutoRight=false;project.Sources[0].Right.Decay.ForEach(k=>k.Y*=.7);
        string baseline=ProjectIO.Serialize(project);var selected=project.Sources[0];var session=new ReflectionEditSession(project,[selected.Id]);
        session.Reference.Decay.ForEach(k=>k.Y*=.5);session.RightGainDelta=2;session.RightTiltDelta=.2;session.AzimuthDelta=2;session.ElevationDelta=3;
        var result=session.Apply(project);
        check(Math.Abs(result.Sources[0].Right.Rt(1000)/selected.Right.Rt(1000)-.5)<1e-12,"Manual R retains independent decay while applying shared ratio");
        check(result.Sources[0].Right.GainDb==selected.Right.GainDb+2&&result.Sources[0].Right.TiltDbPerOct==selected.Right.TiltDbPerOct+.2,"Relative R gain/tilt affects manual excitation");
        check(result.Sources[0].Azimuth==selected.Azimuth+2&&result.Sources[0].Elevation==selected.Elevation+3,"Batch direction offsets apply");
        check(result.Sources.Skip(1).Zip(project.Sources.Skip(1)).All(v=>ProjectIO.Serialize(v.First)==ProjectIO.Serialize(v.Second)),"Unselected sources remain byte-identical");
        var invalid=new ReflectionEditSession(project,[selected.Id]);invalid.Reference.GainDb=24;invalid.RightGainDelta=50;
        var bounded=invalid.Apply(project);bounded.Validate();
        check(invalid.ClampedValues>0&&bounded.Sources[0].Right.GainDb==60&&ProjectIO.Serialize(project)==baseline,"Extreme collective edit clips only saturated values without mutating original");
        bool rejected=false;
        project.OutputDb=-1;check(session.Apply(project).OutputDb==-1,"Global changes remain independent from source edits");project.OutputDb=0;
        project.Sources[0].Left.GainDb+=1;try{session.Apply(project);}catch(InvalidOperationException){rejected=true;}check(rejected,"Changed source baseline rejects stale direct edits");project.Sources[0].Left.GainDb-=1;
        var auto=Presets.BuiltIn[0].Create();var autoEdit=new ReflectionEditSession(auto,[auto.Sources[0].Id]){RightMode=2};
        var beforeRight=auto.Sources[0].EffectiveRight();var manual=autoEdit.Apply(auto);
        check(!manual.Sources[0].AutoRight&&ProjectIO.Serialize(manual.Sources[0].Right)==ProjectIO.Serialize(beforeRight),"Switching R to manual preserves current effective parameters");
        var zeros=Presets.BuiltIn[0].Create();zeros.Sources.ForEach(s=>{s.Left.FirstReflectionExtraPath=0;s.Left.MixingPath=0;});
        var zeroEdit=new ReflectionEditSession(zeros,zeros.Sources.Select(s=>s.Id));zeroEdit.Reference.FirstReflectionExtraPath=.2;zeroEdit.Reference.MixingPath=.3;
        check(zeroEdit.Apply(zeros).Sources.All(s=>s.Left.FirstReflectionExtraPath==.2&&s.Left.MixingPath==.3),"Zero reference distances can be raised without division by zero");
        var shaped=Presets.BuiltIn[3].Create();var shapeEdit=new ReflectionEditSession(shaped,shaped.Sources.Select(s=>s.Id));
        shapeEdit.Reference.Energy.Single(k=>k.X==1000).Y+=2;
        shapeEdit.Reference.Decay.Single(k=>k.X==4000).Y*=.75;
        var local=shapeEdit.Apply(shaped);double maxDb=0,maxRatio=0;
        for(int i=0;i<shaped.Sources.Count;i++)foreach(double f in Enumerable.Range(0,1001).Select(j=>20*Math.Pow(1000,j/1000.0)))
        {
            double expectedDb=shaped.Sources[i].Left.EnergyDb(f)+shapeEdit.Reference.EnergyDb(f)-shapeEdit.Baseline.EnergyDb(f);
            double expectedRt=shaped.Sources[i].Left.Rt(f)*shapeEdit.Reference.Rt(f)/shapeEdit.Baseline.Rt(f);
            maxDb=Math.Max(maxDb,Math.Abs(local.Sources[i].Left.EnergyDb(f)-expectedDb));
            maxRatio=Math.Max(maxRatio,Math.Abs(local.Sources[i].Left.Rt(f)/expectedRt-1));
        }
        check(maxDb<.03&&maxRatio<.003,"Local curve adjustments preserve continuous reference deltas: "+maxDb+" dB / "+maxRatio);
        var generated=Generator.Generate(result);var output=Exporter.Export(generated,folder);
        check(Exporter.ReadWave(TestFiles.Get(output,"Matrix_PATHS_LL_RL_LR_RR.wav")).Channels.Zip(generated.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Shared edited project generates and exports exact preview kernels");
    }
}
