using SoundstageIR.Core;
using System.Numerics;
public static class SpectralChecks
{
    static double At(double[] h,double f,int sr){Complex sum=0,step=Complex.FromPolarCoordinates(1,-2*Math.PI*f/sr),z=1;foreach(double v in h){sum+=v*z;z*=step;}return 20*Math.Log10(sum.Magnitude);}
    public static void Run(Action<bool,string> check,string folder)
    {
        check(new Project().Smooth1==12,"First-stage default is 1/12 octave");
        check(NoiseKernel.TailWindow(-70)==1&&NoiseKernel.TailWindow(-80)==0,"Late-envelope fade endpoints");
        check(Math.Abs(NoiseKernel.TailWindow(-75)-.5)<1e-12,"Late-envelope half-cosine midpoint");
        foreach(int sr in new[]{44100,48000,96000})
        {
            // Narrow infrasonic rise beside the passband: the old 20 Hz extension misses it.
            var h=Dsp.MinimumPhase(f=>Math.Pow(10,14*Math.Exp(-.5*Math.Pow((f-18.8)/.55,2))/20),sr,sr*2);
            var p=new Project{SampleRate=sr,StrictMirror=true,Smooth1=24};
            var raw=new[]{h,new double[h.Length],new double[h.Length],h};
            var r=SpectralSynthesis.Apply(p,raw,raw);
            check(r.Kernels[0].Length==2*h.Length-1,"Bounded frequency-domain output length "+sr);
            check(r.Kernels[0].SequenceEqual(r.Kernels[3])&&r.Kernels[1].SequenceEqual(r.Kernels[2]),"Spectral mirror "+sr);
            check(At(r.Kernels[0],18.8,sr)<0,"19 Hz bump corrected before bandpass "+sr);
            check(At(r.Kernels[0],10,sr)<-65,"Final infrasonic stop "+sr);
            check(Math.Abs(At(r.Kernels[0],20,sr))<1,"20 Hz remains passband "+sr);
            check(Math.Abs(At(r.Kernels[0],1000,sr))<.1,"Midband calibration preserved "+sr);
            check(r.ProjectionErrorDb<.1,"Finite synthesis follows frequency-domain target "+sr);
            // A true direct-only scene must still realize the requested sharp bandpass.
            var flat=new[]{new[]{1.0},new[]{0.0},new[]{0.0},new[]{1.0}};
            var shortResult=SpectralSynthesis.Apply(p,flat,flat);
            check(shortResult.Kernels[0].Length==sr,"Direct-only one-second support "+sr);
            check(Math.Abs(At(shortResult.Kernels[0],20,sr))<.1&&Math.Abs(At(shortResult.Kernels[0],20000,sr))<.1,"Both passband boundaries preserved "+sr);
            check(At(shortResult.Kernels[0],10,sr)<-75&&At(shortResult.Kernels[0],Dsp.BandpassStopHigh(sr),sr)<-75,"Both stopbands preserved "+sr);
            check(Generator.FirstPeak(shortResult.Kernels[0])<sr*.001,"No half-kernel leading delay "+sr);
        }
        var project=Presets.BuiltIn.Single(p=>p.Name=="悠长大厅").Create();var result=Generator.Generate(project);
        check(result.Kernels[0].Length==result.Raw[0].Length+result.CorrectionSupport-1,"Hall length follows scene support");
        check(result.ProjectionErrorDb<.1,"Hall projection error");
        var exported=Exporter.Export(result,folder);var wave=Exporter.ReadWave(TestFiles.Get(exported,"Matrix_PATHS_LL_RL_LR_RR.wav"));
        check(wave.Channels.Zip(result.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Spectral output WAV exact samples");
        check(Math.Abs(Dsp.SmoothedDbAt(Dsp.Sum(result.Kernels[0],result.Kernels[1]),project.SampleRate,project.Smooth1).Concat(Dsp.SmoothedDbAt(Dsp.Sum(result.Kernels[2],result.Kernels[3]),project.SampleRate,project.Smooth1)).Average()-project.OutputDb)<1e-5,"Original common normalization retained");
    }
}
