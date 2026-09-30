using SoundstageIR.Core;
public static class TimingChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        var p=Presets.BuiltIn[0].Create();
        check(!p.StrictMirror,"Defaults: independent mirror random streams");
        check(Presets.BuiltIn.All(b=>b.ExtraPath<=1),"Built-in first reflection extra paths stay short including halls");
        var e=new Excitation{FirstReflectionExtraPath=.5,MixingPath=1};
        check(Math.Abs(e.OnsetMs-1.4577259475)<1e-8&&Math.Abs(e.BuildMs-2.915451895)<1e-8,"Metres convert using extra path only");
        var preset=Presets.BuiltIn[^1];var options=preset.Defaults();options.Rt=2;options.ExtraPath=.75;options.MixingPath=8;options.WetPercent=4;options.AirDistance=20;
        var hall=preset.Create(options);
        check(hall.Sources.All(s=>s.Left.FirstReflectionExtraPath>=.75&&s.Left.FirstReflectionExtraPath<=1.1),"Template extra path profile retains short arrivals");
        check(hall.Sources.Max(s=>s.Left.Rt(1000))<=2&&hall.Sources.Min(s=>s.Left.Rt(1000))<1.8,"Template retains directional decay differences");
        check(hall.ReflectionEnergyPercent==4,"Template energy percentage applied");
        check(hall.Direct.AirAbsorptionDistance==20&&!hall.Direct.AirAbsorption,"Template direct absorption distance independent and optional");
        var q=ProjectIO.Clone(p);q.Direct.AirAbsorptionDistance=20;
        var a=Generator.Generate(p);var b=Generator.Generate(q);
        check(a.ZeroSample==b.ZeroSample&&a.Kernels.Zip(b.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Air distance with absorption disabled adds no delay or sound change");
        check(NoiseKernel.ArrivalProbability(0,.05,48000)>0&&NoiseKernel.ArrivalProbability(.05,.05,48000)==1,"Density transition has immediate arrivals and reaches dense tail");
    }
}
