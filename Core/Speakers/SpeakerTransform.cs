using System.Numerics;
namespace SoundstageIR.Core.Speakers;
public static class SpeakerTransform
{
    // Solve F C = T directly. Common output bandpass/level is divided out before
    // regularization; it is not interpreted as a head notch to invert.
    // Identity anchoring gives C=I exactly when the desired and playback fields coincide.
    public static InverseResult Design(double[][] playback,double[][] target,int sr,double maxDb,CancellationToken ct=default)
    {
        int support=playback.Max(h=>h.Length)+target.Max(h=>h.Length);
        int n=Dsp.Pow2(Math.Max(sr*2,checked(support*2))),bins=n/2+1;
        var f=playback.Select(h=>Dsp.Spectrum(h,n)).ToArray();
        var t=target.Select(h=>Dsp.Spectrum(h,n)).ToArray();
        var scale=new double[bins];var weak=new double[bins];var frequency=new double[bins];
        for(int k=0;k<bins;k++)
        {
            double u=Dsp.Power(f[0][k])+Dsp.Power(f[2][k]),v=Dsp.Power(f[1][k])+Dsp.Power(f[3][k]);
            scale[k]=Math.Sqrt((u+v)*.5);frequency[k]=k*(double)sr/n;
            var z=Complex.Conjugate(f[0][k])*f[1][k]+Complex.Conjugate(f[2][k])*f[3][k];
            double strongest=(u+v+Math.Sqrt((u-v)*(u-v)+4*Dsp.Power(z)))*.5;
            weak[k]=Dsp.Power(f[0][k]*f[3][k]-f[1][k]*f[2][k])/Math.Max(1e-300,strongest*scale[k]*scale[k]);
        }
        var local=Dsp.SmoothPower(frequency,weak,6);double gain=Math.Pow(10,maxDb/20);
        double floor=scale.Max()*1e-4;var spectra=Enumerable.Range(0,4).Select(_=>new Complex[n]).ToArray();
        var recorder=new ResponseRecorder(n,sr);
        for(int k=0;k<bins;k++)
        {
            if(k%2048==0)ct.ThrowIfCancellationRequested();
            double s=Math.Max(1e-150,scale[k]);
            var q=SpeakerInverse.Solve(f[0][k]/s,f[1][k]/s,f[2][k]/s,f[3][k]/s,gain,Math.Max(0,local[k]-weak[k]));
            double w=scale[k]*scale[k]/(scale[k]*scale[k]+floor*floor);
            recorder.Add(k,q,w/s);
            for(int row=0;row<2;row++)for(int col=0;col<2;col++)
            {
                int c=row*2+col;
                var v=(row==col?Complex.One:Complex.Zero)+w*(q[row*2]*(t[col][k]-f[col][k])+q[row*2+1]*(t[2+col][k]-f[2+col][k]))/s;
                spectra[c][k]=k==0||k==n/2?v.Real:v;
                if(k>0&&k<n/2)spectra[c][n-k]=Complex.Conjugate(v);
            }
        }
        foreach(var h in spectra){ct.ThrowIfCancellationRequested();Dsp.Fft(h,true);}
        double total=spectra.Sum(h=>h.Sum(z=>z.Real*z.Real));
        int delay=64,length;double error;double[][] output;
        do
        {
            length=n/2+delay;output=Enumerable.Range(0,4).Select(_=>new double[length]).ToArray();error=0;
            for(int c=0;c<4;c++)for(int i=0;i<n;i++)
            {
                int j=(i+delay)%n;
                double w=j>=length?0:j<32?.5-.5*Math.Cos(Math.PI*j/32):j>length-256?.5-.5*Math.Cos(Math.PI*(length-1-j)/255):1;
                if(j<length)output[c][j]=spectra[c][i].Real*w;
                error+=spectra[c][i].Real*spectra[c][i].Real*(1-w)*(1-w);
            }
            if(error/Math.Max(total,1e-300)<1e-7||delay>=n/4)break;
            delay*=2;ct.ThrowIfCancellationRequested();
        }while(true);
        double tail=0;int end=length-1;
        while(end>delay+256)
        {
            double e=output.Sum(h=>h[end]*h[end]);if(tail+e>total*1e-10)break;tail+=e;end--;
        }
        int retained=Math.Min(length,end+257);
        for(int c=0;c<4;c++)
        {
            Array.Resize(ref output[c],retained);
            for(int i=Math.Max(delay+1,retained-256);i<retained;i++)output[c][i]*=.5-.5*Math.Cos(Math.PI*(retained-1-i)/255);
        }
        double residual=0;
        for(int c=0;c<4;c++)for(int i=0;i<n;i++)
        {int j=(i+delay)%n;double actual=j<retained?output[c][j]:0;residual+=Math.Pow(actual-spectra[c][i].Real,2);}
        return new(output,delay,Dsp.Db(residual/Math.Max(1e-300,total))){Response=recorder.Result};
    }
    // Observe the exact operator K used in C = I + K(T-F), before causal projection.
    // Unwrap at every FFT bin, but retain only a small logarithmic plot grid.
    internal sealed class ResponseRecorder
    {
        readonly Dictionary<int,int> indices;
        readonly double[] previous=new double[4],unwrapped=new double[4];
        readonly double df;
        public InverseResponse Result {get;}
        public ResponseRecorder(int n,int sr)
        {
            df=sr/(double)n;double high=Math.Min(22000,sr*.5*.995);
            var bins=Enumerable.Range(0,1801).Select(i=>Math.Clamp((int)Math.Round(5*Math.Pow(high/5,i/1800.0)/df),1,n/2-1)).Distinct().ToArray();
            indices=bins.Select((b,i)=>(b,i)).ToDictionary(v=>v.b,v=>v.i);
            double[][] Arrays()=>Enumerable.Range(0,4).Select(_=>new double[bins.Length]).ToArray();
            Result=new(bins.Select(b=>b*df).ToArray(),Arrays(),Arrays(),Arrays());
        }
        public void Add(int bin,Complex[] q,double scale)
        {
            bool save=indices.TryGetValue(bin,out int index);
            for(int c=0;c<4;c++)
            {
                var value=q[c]*scale;double phase=Math.Atan2(value.Imaginary,value.Real);
                double delta=bin==0?0:Math.IEEERemainder(phase-previous[c],2*Math.PI);
                unwrapped[c]=bin==0?phase:unwrapped[c]+delta;previous[c]=phase;
                if(!save)continue;
                Result.MagnitudeDb[c][index]=Dsp.Db(Dsp.Power(value));
                Result.PhaseDegrees[c][index]=unwrapped[c]*180/Math.PI;
                Result.GroupDelayMs[c][index]=-delta/(2*Math.PI*df)*1000;
            }
        }
    }

}
