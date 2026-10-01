using System.Numerics;
namespace SoundstageIR.Core;
public sealed record SynthesisResult(double[][] Kernels,double[][] Direct,double[][] EarEq,double[] SecondEq,double[] Bandpass,List<EqReport> Reports,int FftLength,int Support,double ProjectionErrorDb,double DiscardedEnergyDb);
public static class SpectralSynthesis
{
    public static SynthesisResult Apply(Project p,double[][] raw,double[][] direct,CancellationToken ct=default)
    {
        int sr=p.SampleRate,length=raw.Max(h=>h.Length),support=EqDesigner.Support(length,sr);
        int outputLength=checked(length+support-1),n=EqDesigner.FftLength(length,sr);
        var refs=new[]{Dsp.Spectrum(Dsp.Sum(raw[0],raw[1]),n),Dsp.Spectrum(Dsp.Sum(raw[2],raw[3]),n)};
        double strength=p.Equalize?p.EarEqStrengthPercent/100:0;
        var left=EqDesigner.Plan(refs[0],sr,p.Smooth1,strength,"一级左耳 EQ",support,ct);
        var right=p.StrictMirror?left:EqDesigner.Plan(refs[1],sr,p.Smooth1,strength,"一级右耳 EQ",support,ct);
        var first=new[]{left,right};var center=new Complex[n];
        for(int k=0;k<n;k++)center[k]=(refs[0][k]*left.Spectrum[k]+refs[1][k]*right.Spectrum[k])*.5;
        var second=EqDesigner.Plan(center,sr,p.Smooth2,p.Equalize&&!p.StrictMirror?p.CenterEqStrengthPercent/100:0,"二级共同 EQ",support,ct);
        var bandDb=Enumerable.Range(0,n/2+1).Select(k=>Dsp.BandpassDb(k*(double)sr/n,sr)).ToArray();
        var kernels=new double[4][];var finalDirect=new double[4][];double projectionError=0,discardedRatio=0;
        for(int ear=0;ear<2;ear++)
        {
            ct.ThrowIfCancellationRequested();
            // Design one complex correction from the complete target, without truncating stages.
            var totalDb=new double[n/2+1];for(int k=0;k<totalDb.Length;k++)totalDb[k]=first[ear].GainDb[k]+second.GainDb[k]+bandDb[k];
            var correction=EqDesigner.MinimumPhaseSpectrum(totalDb,ct);
            var desiredRef=new Complex[n];for(int k=0;k<n;k++)desiredRef[k]=refs[ear][k]*correction[k];
            for(int input=0;input<2;input++)
            {
                int c=ear*2+input;kernels[c]=Render(raw[c],correction,true);finalDirect[c]=Render(direct[c],correction,false);
            }
            var actual=Dsp.Spectrum(Dsp.Sum(kernels[ear*2],kernels[ear*2+1]),n);
            double high=Dsp.BandpassStopHigh(sr);
            var grid=Enumerable.Range(0,241).Select(i=>10*Math.Pow(2,i/240.0)).Concat(Dsp.Frequencies)
                .Concat(Enumerable.Range(1,32).Select(i=>20000*Math.Pow(high/20000,i/32.0))).ToArray();
            var expectedSmooth=Dsp.SmoothedSpectrumDb(desiredRef,sr,p.Smooth1,grid,10,high);
            var actualSmooth=Dsp.SmoothedSpectrumDb(actual,sr,p.Smooth1,grid,10,high);
            for(int i=0;i<grid.Length;i++)if(expectedSmooth[i]>-60)
                projectionError=Math.Max(projectionError,Math.Abs(expectedSmooth[i]-actualSmooth[i]));
        }
        double[] Render(double[] input,Complex[] correction,bool measure)
        {
            ct.ThrowIfCancellationRequested();var spectrum=Dsp.Spectrum(input,n);
            for(int k=0;k<n;k++)spectrum[k]*=correction[k];Dsp.Fft(spectrum,true);
            var output=new double[outputLength];double total=0,error=0;
            for(int i=0;i<n;i++)
            {
                double sample=spectrum[i].Real,w=i>=outputLength?0:i<outputLength*.9?1:.5+.5*Math.Cos(Math.PI*(i-outputLength*.9)/(outputLength-1-outputLength*.9));
                if(i<outputLength)output[i]=sample*w;
                if(measure){total+=sample*sample;error+=sample*sample*(1-w)*(1-w);}
            }
            if(measure&&total>0)discardedRatio=Math.Max(discardedRatio,error/total);
            return output;
        }
        var reports=new List<EqReport>();if(p.Equalize){reports.Add(left.Report);if(!p.StrictMirror){reports.Add(right.Report);reports.Add(second.Report);}}
        // Full inverse transforms are diagnostic transfer responses, not serial output FIRs.
        double[] Diagnostic(EqSpectrum eq)=>eq.Report.Taps==1?[1.0]:EqDesigner.ToImpulse(eq.Spectrum,n,false);
        return new(kernels,finalDirect,[Diagnostic(left),Diagnostic(right)],Diagnostic(second),
            EqDesigner.ToImpulse(EqDesigner.MinimumPhaseSpectrum(bandDb,ct),n,false),reports,n,support,projectionError,10*Math.Log10(Math.Max(1e-300,discardedRatio)));
    }
}
