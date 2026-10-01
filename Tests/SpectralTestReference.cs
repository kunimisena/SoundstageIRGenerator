using System.Numerics;
using SoundstageIR.Core;
public static class SpectralTestReference
{
    public static bool SameSources(GenerationResult a,GenerationResult b)=>a.DirectPaths.Zip(b.DirectPaths).All(v=>v.First.SequenceEqual(v.Second))
        &&a.Contributions.Zip(b.Contributions).All(v=>v.First.LeftInputKernel.SequenceEqual(v.Second.LeftInputKernel)&&v.First.RightInputKernel.SequenceEqual(v.Second.RightInputKernel)
        &&v.First.EarPaths.Zip(v.Second.EarPaths).All(h=>h.First.Zip(h.Second).All(x=>Math.Abs(x.First/a.ReflectionGain-x.Second/b.ReflectionGain)<1e-10)));
    public static bool FirstEq(GenerationResult r)
    {
        for(int ear=0;ear<2;ear++)
        {
            var response=Dsp.Spectrum(Dsp.Sum(r.Raw[ear*2],r.Raw[ear*2+1]),r.SynthesisFftLength);
            var plan=EqDesigner.Plan(response,r.Project.SampleRate,r.Project.Smooth1,r.Project.EarEqStrengthPercent/100,"test",r.CorrectionSupport);
            if(EqDesigner.ToImpulse(plan.Spectrum,r.SynthesisFftLength,false).Zip(r.EarEq[ear]).Any(v=>Math.Abs(v.First-v.Second)>1e-10))return false;
        }
        return true;
    }
    public static double[] SecondEq(GenerationResult r)
    {
        int n=r.SynthesisFftLength;var left=Dsp.Spectrum(Dsp.Sum(r.Raw[0],r.Raw[1]),n);var right=Dsp.Spectrum(Dsp.Sum(r.Raw[2],r.Raw[3]),n);
        var ql=Dsp.Spectrum(r.EarEq[0],n);var qr=Dsp.Spectrum(r.EarEq[1],n);
        for(int k=0;k<n;k++)left[k]=(left[k]*ql[k]+right[k]*qr[k])*.5;
        var p=r.Project;var result=EqDesigner.Plan(left,p.SampleRate,p.Smooth2,p.CenterEqStrengthPercent/100,"test",r.CorrectionSupport);
        return EqDesigner.ToImpulse(result.Spectrum,n,false);
    }
    public static bool Route(GenerationResult r,int channel,double frequency)
    {
        int n=r.SynthesisFftLength,k=(int)Math.Round(frequency*n/r.Project.SampleRate);
        var source=Dsp.Spectrum(r.Raw[channel],n)[k];
        var first=Dsp.Spectrum(r.EarEq[channel/2],n)[k];var second=Dsp.Spectrum(r.SecondEq,n)[k];var band=Dsp.Spectrum(r.Bandpass,n)[k];
        var expected=source*first*second*band*Math.Pow(10,r.CommonGainDb/20);var actual=Dsp.Spectrum(r.Kernels[channel],n)[k];
        return (expected-actual).Magnitude<.001*Math.Max(1,expected.Magnitude);
    }
}
