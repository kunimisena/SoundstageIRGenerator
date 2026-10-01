using System.Numerics;
namespace SoundstageIR.Core;
public sealed record WetBalanceReport(double GainDb,double PredictedPercent,int Evaluations,bool Converged);
// Solve one broadband amplitude multiplier. No per-band or per-source wet correction.
public static class WetBalanceSolver
{
    public static WetBalanceReport Solve(Project p,double[][] direct,double[][] wet,CancellationToken ct=default,Action<string>? progress=null)
    {
        ct.ThrowIfCancellationRequested();
        double dryEnergy=EnergyBalance.Energy(direct,p.SampleRate),wetEnergy=EnergyBalance.Energy(wet,p.SampleRate);
        if(dryEnergy<=0||wetEnergy<=0||p.ReflectionEnergyPercent is <=0 or >=100)
            return new(0,EnergyBalance.Percent(dryEnergy,wetEnergy),0,true);
        ct.ThrowIfCancellationRequested();
        int sr=p.SampleRate,length=Math.Max(direct.Max(h=>h.Length),wet.Max(h=>h.Length)),n=EqDesigner.FftLength(length,sr),half=n/2;
        var dRef=new Complex[2][];var rRef=new Complex[2][];
        var dryPower=new double[2][];var wetPower=new double[2][];
        var smoothDry=new double[2][];var smoothWet=new double[2][];var smoothCross=new double[2][];
        double[] frequencies=EqDesigner.DesignFrequencies(sr);double high=Dsp.BandpassStopHigh(sr);
        for(int ear=0;ear<2;ear++)
        {
            dRef[ear]=new Complex[half+1];rRef[ear]=new Complex[half+1];
            dryPower[ear]=new double[half+1];wetPower[ear]=new double[half+1];
            for(int input=0;input<2;input++)
            {
                ct.ThrowIfCancellationRequested();int c=ear*2+input;
                Add(direct[c],dRef[ear],dryPower[ear]);Add(wet[c],rRef[ear],wetPower[ear]);
            }
            var a=new double[half+1];var b=new double[half+1];var cross=new double[half+1];
            for(int k=0;k<=half;k++){a[k]=Dsp.Power(dRef[ear][k]);b[k]=Dsp.Power(rRef[ear][k]);cross[k]=(dRef[ear][k]*Complex.Conjugate(rRef[ear][k])).Real;}
            smoothDry[ear]=Smooth(a,p.Smooth1);smoothWet[ear]=Smooth(b,p.Smooth1);smoothCross[ear]=Smooth(cross,p.Smooth1);
        }
        void Add(double[] h,Complex[] reference,double[] energy)
        {
            var spectrum=Dsp.Spectrum(h,n);
            for(int k=0;k<=half;k++){reference[k]+=spectrum[k];energy[k]+=Dsp.Power(spectrum[k]);}
        }
        double[] Smooth(double[] power,int denominator)=>Dsp.SmoothedPowerSpectrum(power,sr,denominator,frequencies,10,high);
        double strength=p.Equalize?p.EarEqStrengthPercent/100:0;
        double secondStrength=p.Equalize&&!p.StrictMirror?p.CenterEqStrengthPercent/100:0;
        var bandDb=Enumerable.Range(0,half+1).Select(k=>Dsp.BandpassDb(k*(double)sr/n,sr)).ToArray();
        double target=p.ReflectionEnergyPercent/100,targetLog=Math.Log(target/(1-target));int evaluations=0;
        (double Db,double Error,double Percent) Evaluate(double db)
        {
            ct.ThrowIfCancellationRequested();evaluations++;
            progress?.Invoke($"预修正混响占比 · 频谱计算 {evaluations}");
            double g=Math.Pow(10,db/20);
            if(!double.IsFinite(g*g)||g==0)throw new ArithmeticException("混响预修正超出双精度范围。");
            var first=new double[2][];
            for(int ear=0;ear<2;ear++)
            {
                if(ear==1&&p.StrictMirror){first[ear]=first[0];continue;}
                var targetDb=new double[frequencies.Length];
                for(int i=0;i<targetDb.Length;i++)
                {
                    double power=smoothDry[ear][i]+g*g*smoothWet[ear][i]+2*g*smoothCross[ear][i];
                    // Roundoff can make the quadratic slightly negative at exact cancellation.
                    targetDb[i]=strength==0?0:-strength*10*Math.Log10(Math.Max(1e-300,power));
                }
                first[ear]=EqDesigner.InterpolateGain(targetDb,frequencies,n,sr);
            }
            var second=new double[half+1];
            if(secondStrength>0)
            {
                // The second reference is a complex ear sum: retain first-stage phase here.
                var left=EqDesigner.MinimumPhaseSpectrum(first[0],ct);var right=EqDesigner.MinimumPhaseSpectrum(first[1],ct);
                var centerPower=new double[half+1];
                for(int k=0;k<=half;k++)centerPower[k]=Dsp.Power(((dRef[0][k]+g*rRef[0][k])*left[k]+(dRef[1][k]+g*rRef[1][k])*right[k])*.5);
                var centerDb=Smooth(centerPower,p.Smooth2).Select(v=>-secondStrength*10*Math.Log10(Math.Max(1e-300,v))).ToArray();
                second=EqDesigner.InterpolateGain(centerDb,frequencies,n,sr);
            }
            double df=(double)sr/n,d=0,r=0,offset=double.NegativeInfinity;
            int lo=Math.Max(1,(int)Math.Floor(20/df-.5)),hi=Math.Min(half,(int)Math.Ceiling(20000/df+.5));
            for(int ear=0;ear<2;ear++)for(int k=lo;k<=hi;k++)offset=Math.Max(offset,first[ear][k]+second[k]+bandDb[k]);
            for(int ear=0;ear<2;ear++)for(int k=lo;k<=hi;k++)
            {
                double weight=Math.Max(0,Math.Min((k+.5)*df,20000)-Math.Max((k-.5)*df,20))/df;
                double q=weight*Math.Pow(10,(first[ear][k]+second[k]+bandDb[k]-offset)/10);
                d+=q*dryPower[ear][k];r+=q*wetPower[ear][k];
            }
            double ratioLog=Math.Log(r/d)+2*Math.Log(g),error=ratioLog-targetLog;
            if(!double.IsFinite(error))throw new ArithmeticException("混响占比无法有效估计。");
            double percent=ratioLog>=0?100/(1+Math.Exp(-ratioLog)):100*Math.Exp(ratioLog)/(1+Math.Exp(ratioLog));
            return(db,error,percent);
        }
        var best=Evaluate(0);var start=best;bool converged=Math.Abs(best.Error)<1e-5;
        if(!converged)
        {
            // Bracket in log gain; expansion is a solver step, not an audio gain cap.
            var other=start;double step=Math.Max(3,Math.Abs(start.Error)*10/Math.Log(10));bool bracket=false;
            for(int i=0;i<12;i++)
            {
                try{other=Evaluate(-Math.Sign(start.Error)*step);}catch(ArithmeticException){break;}
                if(Math.Abs(other.Error)<Math.Abs(best.Error))best=other;
                if(Math.Abs(best.Error)<1e-5){converged=true;break;}
                if(Math.Sign(other.Error)!=Math.Sign(start.Error)){bracket=true;break;}
                step*=2;
            }
            if(bracket&&!converged)
            {
                var lo=start.Db<other.Db?start:other;var hi=start.Db<other.Db?other:start;
                for(int i=0;i<36;i++)
                {
                    double x=(lo.Db*hi.Error-hi.Db*lo.Error)/(hi.Error-lo.Error);
                    x=Math.Clamp(x,lo.Db+.1*(hi.Db-lo.Db),hi.Db-.1*(hi.Db-lo.Db));
                    var value=Evaluate(x);if(Math.Abs(value.Error)<Math.Abs(best.Error))best=value;
                    if(Math.Abs(best.Error)<1e-5){converged=true;break;}
                    if(Math.Sign(value.Error)==Math.Sign(lo.Error))lo=value;else hi=value;
                }
            }
        }
        return new(best.Db,best.Percent,evaluations,converged);
    }
}
