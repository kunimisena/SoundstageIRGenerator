using StatisticalField.Core;
using System.Globalization;
public static class EnergyChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        var p=Presets.BuiltIn[0].Create();
        check(p.Smooth1==24&&p.Smooth2==3,"EQ defaults 1/24 octave per ear, 1/3 octave combined");
        check(new ReflectionPair().Left.GainDb==0,"New source uses virtual 0 dB reference");
        var flat=ProjectIO.Clone(p);foreach(var s in flat.Sources){s.Left.GainDb=0;s.Right.GainDb=0;}
        // -6 dB wet/dry ENERGY ratio corresponds to this component percentage.
        flat.ReflectionEnergyPercent=100*Math.Pow(10,-.6)/(1+Math.Pow(10,-.6));
        var baseline=Generator.Generate(flat);
        check(Math.Abs(baseline.ReflectionPercentBeforeEq-flat.ReflectionEnergyPercent)<1e-8,"Measured global energy ratio equals requested percentage");
        var shifted=ProjectIO.Clone(flat);foreach(var s in shifted.Sources){s.Left.GainDb-=6;s.Right.GainDb-=6;}
        var adjusted=Generator.Generate(shifted);
        check(baseline.Kernels.Zip(adjusted.Kernels).Max(c=>c.First.Zip(c.Second).Max(v=>Math.Abs(v.First-v.Second)))<2e-7,"ALL source weights -6 dB equals ALL 0 dB at fixed global wet/dry -6 dB");
        check(shifted.Sources.All(s=>s.Left.GainDb==-6),"No source is silently rebased to 0 dB");
        foreach(int count in new[]{6,12})
        {
            var n=ProjectIO.Clone(p);n.Sources=Templates.Create(Distribution.Ring,count,0,0,90,new Excitation(),"count");var r=Generator.Generate(n);
            check(Math.Abs(r.ReflectionPercentBeforeEq-p.ReflectionEnergyPercent)<1e-8,"Direction count does not change overall reflection energy "+count);
        }
        foreach(double ratio in new[]{0.0,100})
        {var n=ProjectIO.Clone(p);n.ReflectionEnergyPercent=ratio;var r=Generator.Generate(n);check(Math.Abs(r.ReflectionPercentBeforeEq-ratio)<1e-8&&r.Kernels.All(x=>x.All(double.IsFinite)),"Finite pure dry / pure wet endpoint "+ratio);}
        var noDirect=ProjectIO.Clone(p);noDirect.Direct.Enabled=false;check(Generator.Generate(noDirect).ReflectionPercentBeforeEq==100,"Disabled direct produces pure reflection");

    }
}
