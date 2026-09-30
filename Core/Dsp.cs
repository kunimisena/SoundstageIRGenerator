using System.Numerics;
using System.Security.Cryptography;
using System.Text;
namespace SoundstageIR.Core;
public static class Dsp
{
    public static int Pow2(int n){if(n<1||n>0x40000000)throw new ArgumentOutOfRangeException(nameof(n),"FFT 长度超出平台容量。");int p=1;while(p<n)p<<=1;return p;}
    public static void Fft(Complex[] a,bool inverse=false)
    {
        int n=a.Length;if((n&(n-1))!=0)throw new ArgumentException("FFT length");
        for(int i=1,j=0;i<n;i++){int bit=n>>1;for(;(j&bit)!=0;bit>>=1)j^=bit;j^=bit;if(i<j)(a[i],a[j])=(a[j],a[i]);}
        for(int len=2;len<=n;len<<=1){var step=Complex.FromPolarCoordinates(1,(inverse?2:-2)*Math.PI/len);for(int i=0;i<n;i+=len){Complex w=1;for(int j=0;j<len/2;j++){var u=a[i+j];var v=a[i+j+len/2]*w;a[i+j]=u+v;a[i+j+len/2]=u-v;w*=step;}}}
        if(inverse)for(int i=0;i<n;i++)a[i]/=n;
    }
    public static Complex[] Spectrum(double[] h,int n=0){var a=new Complex[n==0?Pow2(h.Length):n];for(int i=0;i<h.Length;i++)a[i]=h[i];Fft(a);return a;}
    public static double[] Convolve(double[] x,double[] h)
    {
        if(h.Length==1)return x.Select(v=>v*h[0]).ToArray();
        int len=x.Length+h.Length-1,n=Pow2(len);var a=Spectrum(x,n);var b=Spectrum(h,n);for(int i=0;i<n;i++)a[i]*=b[i];Fft(a,true);var y=new double[len];for(int i=0;i<len;i++)y[i]=a[i].Real;return y;
    }
    public static double[] Sum(double[] a,double[] b,double scale=1){var y=new double[Math.Max(a.Length,b.Length)];for(int i=0;i<y.Length;i++)y[i]=((i<a.Length?a[i]:0)+(i<b.Length?b[i]:0))*scale;return y;}
    public static double Power(Complex z)=>z.Real*z.Real+z.Imaginary*z.Imaginary;
    public static double Db(double power)=>10*Math.Log10(Math.Max(1e-20,power));
    public static double[] MinimumPhase(Func<double,double> magnitude,int sr,int taps=4096)
    {
        int n=Pow2(Math.Max(32768,taps*8));var a=new Complex[n];
        for(int i=0;i<=n/2;i++){double v=Math.Log(Math.Max(1e-10,magnitude((double)i*sr/n)));a[i]=v;if(i>0&&i<n/2)a[n-i]=v;}
        Fft(a,true);for(int i=1;i<n/2;i++)a[i]*=2;for(int i=n/2+1;i<n;i++)a[i]=0;Fft(a);for(int i=0;i<n;i++)a[i]=Complex.Exp(a[i]);Fft(a,true);
        var h=new double[taps];for(int i=0;i<taps;i++){double w=i<taps*.9?1:.5+.5*Math.Cos(Math.PI*(i-taps*.9)/(taps-1-taps*.9));h[i]=a[i].Real*w;}return h;
    }
    public static double[] SmoothPower(double[] f,double[] p,int denominator)
    {
        var sum=new double[p.Length+1];for(int i=0;i<p.Length;i++)sum[i+1]=sum[i]+p[i];var y=new double[p.Length];double half=.5/denominator;int l=0,r=0;
        for(int i=0;i<p.Length;i++){double lo=f[i]*Math.Pow(2,-half),hi=f[i]*Math.Pow(2,half);while(l<p.Length-1&&f[l]<lo)l++;r=Math.Max(r,l);while(r<p.Length&&f[r]<=hi)r++;y[i]=(sum[r]-sum[l])/Math.Max(1,r-l);}return y;
    }
    public static readonly double[] Frequencies=Enumerable.Range(0,1201).Select(i=>20*Math.Pow(1000,i/1200.0)).ToArray();
    public static double[] PowerAt(double[] h,int sr,double[]? frequencies=null)
    {
        var freq=frequencies??Frequencies;var fft=Spectrum(h,Pow2(Math.Max(h.Length,65536)));var p=new double[freq.Length];
        for(int i=0;i<p.Length;i++){double bin=freq[i]*fft.Length/sr;int j=Math.Min(fft.Length/2-1,(int)bin);double t=bin-j;
            // Interpolate power, never complex bins: common delays must not change plotted magnitude.
            p[i]=Power(fft[j])*(1-t)+Power(fft[j+1])*t;}
        return p;
    }
    // Integrate local, positive powers. Subtracting large global prefix sums loses quiet bands.
    public static double[] SmoothedPowerAt(double[] h,int sr,int denominator)
        =>SmoothedDbAt(h,sr,denominator).Select(db=>Math.Pow(10,db/10)).ToArray();
    public static double[] SmoothedDbAt(double[] h,int sr,int denominator)
    {
        if(denominator<=0)throw new ArgumentOutOfRangeException(nameof(denominator));
        if(h.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException("响应包含非有限数值。");
        double peak=h.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if(peak==0)return Enumerable.Repeat(double.NegativeInfinity,Frequencies.Length).ToArray();
        var spectrum=Spectrum(h.Select(v=>v/peak).ToArray(),Pow2(Math.Max(h.Length,checked(sr*4))));
        int half=spectrum.Length/2;double df=(double)sr/spectrum.Length,offset=20*Math.Log10(peak),width=Math.Pow(2,.5/denominator);
        var power=new double[half+1];var cellWidth=new double[half+1];
        for(int k=1;k<=half;k++){power[k]=Power(spectrum[k]);cellWidth[k]=Math.Log((k+.5)/(k-.5));}
        var output=new double[Frequencies.Length];
        for(int i=0;i<output.Length;i++)
        {
            double lo=Math.Max(.5,Math.Max(20,Frequencies[i]/width)/df),hi=Math.Min(half+.5,Math.Min(20000,Frequencies[i]*width)/df),sum=0;
            int first=Math.Max(1,(int)Math.Floor(lo+.5)),last=Math.Min(half,(int)Math.Ceiling(hi-.5));
            for(int k=first;k<=last;k++)
            {
                double a=Math.Max(lo,k-.5),b=Math.Min(hi,k+.5);
                if(b>a)sum+=power[k]*(a==k-.5&&b==k+.5?cellWidth[k]:Math.Log(b/a));
            }
            output[i]=10*Math.Log10(sum/Math.Log(hi/lo))+offset;
        }
        return output;
    }
    public static double[] OutputBandpass(int sr)
    {
        double stopHigh=Math.Min(22000,sr*.5*.995);
        double Magnitude(double f)
        {
            double db=0;
            if(f<20){double u=Math.Clamp((f-10)/10,0,1);db=-100*(.5+.5*Math.Cos(Math.PI*u));}
            else if(f>20000){double u=Math.Clamp((f-20000)/(stopHigh-20000),0,1);db=-100*(.5-.5*Math.Cos(Math.PI*u));}
            return Math.Pow(10,db/20);
        }
        // One second of causal minimum-phase response resolves the steep infrasonic edge.
        // This is tail support, not a one-second leading delay.
        return MinimumPhase(Magnitude,sr,sr);
    }
    public static double[] DesignEq(double[] response,int sr,int denominator,double strength=1)
        =>EqDesigner.Design(response,sr,denominator,strength).Impulse;

}
public sealed class Gaussian
{
    ulong state;double spare;bool available;
    public Gaussian(long seed,Guid id,int stream,int band)
    {var b=SHA256.HashData(Encoding.UTF8.GetBytes($"SFS-v1/{seed}/{id:D}/{stream}/{band}"));state=BitConverter.ToUInt64(b);}
    ulong Next(){ulong z=(state+=0x9E3779B97F4A7C15UL);z=(z^(z>>30))*0xBF58476D1CE4E5B9UL;z=(z^(z>>27))*0x94D049BB133111EBUL;return z^(z>>31);}
    public double Uniform()=>((Next()>>11)+.5)*(1.0/9007199254740992.0);
    public double NextNormal(){if(available){available=false;return spare;}double r=Math.Sqrt(-2*Math.Log(Uniform())),a=2*Math.PI*Uniform();spare=r*Math.Sin(a);available=true;return r*Math.Cos(a);}
}
public static class NoiseKernel
{
    public static double[] Centers(int sr){var a=new List<double>();for(double f=20;f<sr/2.0;f*=Math.Pow(2,1.0/3))a.Add(f);a.Add(sr/2.0);return a.ToArray();}
    public static double EndSeconds(Excitation e)
    {var shape=e.Envelope;double slope=Math.Min(-6,(shape[^1].Y-shape[^2].Y)/(shape[^1].X-shape[^2].X));return (e.OnsetMs+e.BuildMs)/1000+e.Decay.Max(k=>k.Y)*(1+20/-slope)+.02;}
    public static double EnvelopeDb(Excitation e,double u)
    {
        var shape=e.Envelope;
        if(u<=1)return Curves.At(shape,u);
        double slope=Math.Min(-6,(shape[^1].Y-shape[^2].Y)/(shape[^1].X-shape[^2].X));
        return -60+slope*(u-1);
    }
    public static double[] Generate(Excitation e,int sr,long seed,Guid id,int stream,CancellationToken ct=default)
    {
        int len=(int)Math.Ceiling(EndSeconds(e)*sr),n=Dsp.Pow2(len);var output=new double[len];var centers=Centers(sr);
        // Shared arrival mask across bands; independent Gaussian amplitudes per band.
        // E[mask^2]=1 keeps spectral energy independent of arrival density.
        double[]? mask=null;
        if(e.BuildMs>0)
        {
            mask=new double[n];var arrivals=new Gaussian(seed,id,stream,-1);
            for(int i=0;i<n;i++)
            {
                double probability=ArrivalProbability((double)i/sr-e.OnsetMs/1000,e.BuildMs/1000,sr);
                mask[i]=arrivals.Uniform()<probability?1/Math.Sqrt(probability):0;
            }
        }
        var fullLut=Enumerable.Range(0,12289).Select(i=>EnvelopeDb(e,i/4096.0-1)).ToArray();
        double FullShape(double u){double q=(u+1)*4096;int j=(int)q;return j>=0&&j<12288?fullLut![j]+(fullLut[j+1]-fullLut[j])*(q-j):EnvelopeDb(e,u);}
        for(int band=0;band<centers.Length;band++)
        {
            ct.ThrowIfCancellationRequested();double fc=centers[band],rt=e.Rt(fc),on=e.OnsetMs/1000,build=e.BuildMs/1000;int start=(int)Math.Ceiling(on*sr);
            var a=new Complex[n];var random=new Gaussian(seed,id,stream,band);for(int i=0;i<n;i++)a[i]=random.NextNormal()*(mask?[i]??1);Dsp.Fft(a);
            double weightSum=0;
            for(int k=0;k<=n/2;k++)
            {
                double f=(double)k*sr/n,w=Weight(centers,band,f);weightSum+=w*w*(k==0||k==n/2?1:2);a[k]*=w;if(k>0&&k<n/2)a[n-k]=Complex.Conjugate(a[k]);
            }
            Dsp.Fft(a,true);
            // White noise has unit time variance. Expected band energy equals its spectral measure.
            double norm=0;var envelope=new double[len-start];
            for(int i=start;i<len;i++)
            {
                double t=(double)i/sr-on,v;
                v=Math.Pow(10,FullShape(build>0&&t<build?t/build-1:(t-build)/rt)/20);
                double remaining=(len-1-i)/(sr*.02);if(remaining<1)v*=.5-.5*Math.Cos(Math.PI*Math.Max(0,remaining));
                envelope[i-start]=v;norm+=v*v;
            }
            // Unit impulse spectral energy convention: E[|G(f)|²] follows the requested energy curve.
            double gain=Math.Sqrt(Math.Pow(10,e.EnergyDb(fc)/10)/Math.Max(norm,1e-30));
            for(int i=start;i<len;i++)output[i]+=a[i].Real*envelope[i-start]*gain;
        }
        return output;
    }
    public static double ArrivalProbability(double relativeTime,double buildSeconds,int sr)
    {
        if(buildSeconds<=0)return 1;
        double u=Math.Clamp(relativeTime/buildSeconds,0,1),blend=.5-.5*Math.Cos(Math.PI*u);
        double initial=Math.Min(1,1000.0/sr);
        return initial+(1-initial)*blend;
    }
    public static double Weight(double[] centers,int band,double f)
    {
        if(f<=centers[0])return band==0?1:0;if(f>=centers[^1])return band==centers.Length-1?1:0;
        if(band>0&&f>=centers[band-1]&&f<=centers[band]){double t=Math.Log(f/centers[band-1])/Math.Log(centers[band]/centers[band-1]);return Math.Sin(t*Math.PI/2);}
        if(band<centers.Length-1&&f>=centers[band]&&f<=centers[band+1]){double t=Math.Log(f/centers[band])/Math.Log(centers[band+1]/centers[band]);return Math.Cos(t*Math.PI/2);}
        return 0;
    }
}
