using System.Numerics;
using System.Text.Json.Nodes;
using SoundstageIR.Core;
public static class EqStrengthChecks
{
    static Complex At(double[] h,double f,int sr){Complex z=0;for(int i=0;i<h.Length;i++)z+=h[i]*Complex.FromPolarCoordinates(1,-2*Math.PI*f*i/sr);return z;}
    public static void Run(Action<bool,string> check,string folder)
    {
        check(new Project().CenterEqStrengthPercent==0&&new Project().EarEqStrengthPercent==100,"New project defaults to center bypass with first stage retained");
        foreach(var preset in Presets.BuiltIn)check(preset.Create().CenterEqStrengthPercent==0,"Built-in preset center bypass: "+preset.Name);
        var p=Presets.BuiltIn[0].Create();p.Sources=p.Sources.Take(1).ToList();
        var defaultAudio=Generator.Generate(p);
        check(defaultAudio.SecondEq.SequenceEqual(new[]{1.0})&&defaultAudio.EarEq.All(h=>h.Length>1),"New preset actually bypasses second EQ and retains first EQ");
        check(ProjectIO.Deserialize(ProjectIO.Serialize(p)).CenterEqStrengthPercent==0,"Saved zero strength reopens as zero");
        var savedPartial=ProjectIO.Clone(p);savedPartial.CenterEqStrengthPercent=20;
        check(ProjectIO.Deserialize(ProjectIO.Serialize(savedPartial)).CenterEqStrengthPercent==20,"Existing personal 20 percent setting remains unchanged");
        p.CenterEqStrengthPercent=100;
        foreach(int sr in new[]{44100,48000,96000})
        {
            double[] h=[1.0,.45,-.12];
            var full=Dsp.DesignEq(h,sr,3);var half=Dsp.DesignEq(h,sr,3,.5);var zero=Dsp.DesignEq(h,sr,3,0);
            check(zero.SequenceEqual(new[]{1.0}),"Zero strength is exact impulse "+sr);
            check(full.SequenceEqual(Dsp.DesignEq(h,sr,3,1)),"Full strength retains exact original filter "+sr);
            foreach(double f in new[]{100.0,1000,8000,16000})
            {
                var a=At(full,f,sr);var b=At(half,f,sr);
                check(Math.Abs(20*Math.Log10(b.Magnitude)-10*Math.Log10(a.Magnitude))<.02,"Half strength halves correction dB "+sr+"/"+f);
                check((b*b-a).Magnitude/Math.Max(.01,a.Magnitude)<.003,"Scaled minimum-phase response preserves complex relation "+sr+"/"+f);
            }
        }
        var reference=Generator.Generate(p);
        p.CenterEqStrengthPercent=0;var stage1=Generator.Generate(p);
        check(stage1.SecondEq.SequenceEqual(new[]{1.0})&&stage1.EarEq.Zip(reference.EarEq).All(v=>v.First.SequenceEqual(v.Second)),"Second-stage bypass preserves first-stage EQ");
        check(stage1.Raw.Zip(reference.Raw).All(v=>v.First.SequenceEqual(v.Second)),"EQ strength leaves statistical kernels unchanged");
        p.EarEqStrengthPercent=0;var allZero=Generator.Generate(p);p.Equalize=false;var bypass=Generator.Generate(p);
        check(allZero.Kernels.Zip(bypass.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Both strengths zero equals master bypass sample for sample");
        p.Equalize=true;p.EarEqStrengthPercent=35;p.CenterEqStrengthPercent=50;
        var partial=Generator.Generate(p);
        var left=Dsp.Convolve(Dsp.Sum(partial.Raw[0],partial.Raw[1]),partial.EarEq[0]);
        var right=Dsp.Convolve(Dsp.Sum(partial.Raw[2],partial.Raw[3]),partial.EarEq[1]);
        var expected=Dsp.DesignEq(Dsp.Sum(left,right,.5),p.SampleRate,p.Smooth2,.5);
        check(expected.Length==partial.SecondEq.Length&&expected.Zip(partial.SecondEq).Max(v=>Math.Abs(v.First-v.Second))<1e-10,"Second stage recalculated after partial first-stage correction");
        var exported=Exporter.Export(partial,folder);
        var read=ProjectIO.Load(TestFiles.Get(exported,"project.json"));
        check(read.EarEqStrengthPercent==35&&read.CenterEqStrengthPercent==50,"Project and export retain both strengths");
        check(Exporter.ReadWave(TestFiles.Get(exported,"Matrix_PATHS_LL_RL_LR_RR.wav")).Channels.Zip(partial.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Partial EQ exported audio equals preview");
        p.StrictMirror=true;var strict=Generator.Generate(p);p.CenterEqStrengthPercent=0;var ignored=Generator.Generate(p);
        check(strict.Kernels.Zip(ignored.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Strict mirror ignores dormant second-stage setting");
        check(strict.Kernels[0].SequenceEqual(strict.Kernels[3])&&strict.Kernels[1].SequenceEqual(strict.Kernels[2]),"Partial first stage preserves exact mirror");
        var invalid=ProjectIO.Clone(p);invalid.EarEqStrengthPercent=-10;invalid.CenterEqStrengthPercent=400;
        EditingLimits.Normalize(invalid);invalid.Validate();
        check(invalid.EarEqStrengthPercent==0&&invalid.CenterEqStrengthPercent==100,"Strength edit clamps 0 to 100");
    }
}
