using SoundstageIR.Core;
using System.Text.Json;
public static class FinalPresetChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        var wide=Presets.BuiltIn.Single(p=>p.Name=="宽阔监听");var p=wide.Create();
        check(p.ReflectionEnergyPercent==8&&p.Direct.AirAbsorption&&p.Direct.AirAbsorptionDistance==5&&p.Smooth1==12&&p.CenterEqStrengthPercent==0,"Recommended monitor retains approved balance, air loss and EQ");
        check(ProjectIO.Serialize(p)==ProjectIO.Serialize(wide.Create(wide.Defaults())),"Default options preserve exact baseline");
        var altered=wide.Defaults();altered.Rt*=.5;var shortP=wide.Create(altered);
        check(shortP.Sources.Zip(p.Sources).All(v=>v.First.Id==v.Second.Id&&v.First.Left.Decay.Zip(v.Second.Left.Decay).All(k=>Math.Abs(k.First.Y-k.Second.Y*.5)<1e-12)),"Recommendation remains editable with stable random identities");
        shortP.Sources[0].Left.Energy[0].Y=99;
        check(ProjectIO.Serialize(p)==ProjectIO.Serialize(wide.Create()),"Factory returns independent mutable project instances");
        var original=Path.Combine(folder,"favorite-reference.json");
        if(File.Exists(original))
        {
            var expected=ProjectIO.Load(original);
            check(p.Sources.Zip(expected.Sources).All(v=>v.First.Id==v.Second.Id&&Math.Abs(v.First.Left.FirstReflectionExtraPath-v.Second.Left.FirstReflectionExtraPath)<=.0005),"Favorite identities preserved; default distances actually rounded within half a millimetre");
            foreach(var source in expected.Sources.Concat(expected.TemplateSources))foreach(var e in new[]{source.Left,source.Right})
            {
                e.FirstReflectionExtraPath=Math.Round(e.FirstReflectionExtraPath,3);e.MixingPath=Math.Round(e.MixingPath,3);
                foreach(var curve in new[]{e.Energy,e.Decay,e.Envelope!})foreach(var k in curve){k.X=Math.Round(k.X,3);k.Y=Math.Round(k.Y,3);}
            }
            check(ProjectIO.Serialize(p)==ProjectIO.Serialize(expected),"Recommended defaults match favorite configuration with requested decimal rounding only");
            var a=Generator.Generate(p);var b=Generator.Generate(expected);
            check(a.Kernels.Zip(b.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"All four recommended WAV paths are sample-identical to rounded reference configuration");
        }
        foreach(var preset in Presets.BuiltIn)
        {
            var created=preset.Create();
            check(ProjectIO.Serialize(created)==ProjectIO.Serialize(preset.Create(preset.Defaults())),"Explicit and implicit defaults agree: "+preset.Name);
            check(created.Sources.All(s=>new[]{s.Left,s.Right}.All(e=>e.FirstReflectionExtraPath==Math.Round(e.FirstReflectionExtraPath,3)&&e.MixingPath==Math.Round(e.MixingPath,3)&&e.Decay.All(k=>k.Y==Math.Round(k.Y,3)))),"Stored built-in distances and decay defaults rounded: "+preset.Name);
        }
        var metrics=new List<object>();
        foreach(var name in new[]{"控制室","自由场"})foreach(var rate in new[]{44100,48000,96000})
        {
            var project=Presets.BuiltIn.Single(x=>x.Name==name).Create();project.SampleRate=rate;
            var result=Generator.Generate(project);
            check(result.Kernels.All(h=>h.All(double.IsFinite))&&result.ZeroSample/rate<.002,$"{name} finite output and short common lead-in {rate}");
            check(result.Kernels[0].Any(v=>v!=0)&&result.Kernels[1].Any(v=>v!=0),$"{name} head model retains ipsilateral and contralateral sound {rate}");
            if(name=="自由场")
            {
                check(project.Sources.Count==0&&result.Contributions.Count==0&&result.ReflectionPercentBeforeEq==0,$"Free field has no reflection contribution {rate}");
                project.Seed++;
                check(result.Kernels.Zip(Generator.Generate(project).Kernels).All(v=>v.First.SequenceEqual(v.Second)),$"Free field independent of random seed {rate}");
            }
            else
            {
                check(result.Contributions.Count>0&&Math.Abs(result.ReflectionPercentAfterEq-5)<.02,$"Control room actual integrated wet fraction is 5 percent {rate}");
                var late=result.Contributions.Sum(c=>c.LeftInputKernel.Skip((int)(rate*.1)).Sum(v=>v*v)+c.RightInputKernel.Skip((int)(rate*.1)).Sum(v=>v*v));
                var all=result.Contributions.Sum(c=>c.LeftInputKernel.Sum(v=>v*v)+c.RightInputKernel.Sum(v=>v*v));
                check(late/all<.01,$"Control room source tail energy after 100 ms below 1 percent {rate}");
            }
            var output=Exporter.Export(result,folder);var wav=Exporter.ReadWave(TestFiles.Get(output,"Matrix_PATHS_LL_RL_LR_RR.wav"));
            check(wav.SampleRate==rate&&wav.Channels.Zip(result.Kernels).All(v=>v.First.SequenceEqual(v.Second)),$"{name} exported routing and samples match actual result {rate}");
            metrics.Add(new{name,rate,result.Duration,result.ZeroSample,result.ReflectionPercentBeforeEq,result.ReflectionPercentAfterEq,result.CommonGainDb,result.EqResidualDb,output});
        }
        File.WriteAllText(Path.Combine(folder,"preset-metrics.json"),JsonSerializer.Serialize(metrics,new JsonSerializerOptions{WriteIndented=true}));
    }
}
