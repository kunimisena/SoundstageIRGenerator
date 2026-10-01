using System.Numerics;
namespace SoundstageIR.Core.Speakers;
/// <summary>Rigid-sphere range ratio; spherical Hankel-2 series (FFT sign convention).
/// Duda & Martens, JASA 104, 3048 (1998). FABIAN supplies anatomy at the 1.7 m reference.
/// This ratio changes range, not the reference pinna/torso response.</summary>
public static class SphereRange
{
    public const double ReferenceDistance=1.7;
    public static Complex Ratio(double frequency,double radius,double distance,double cosine)
    {
        if(Math.Abs(distance-ReferenceDistance)<1e-12)return Complex.One;
        // DC is the low-frequency limit; avoid singular Hankel arguments.
        double k=2*Math.PI*Math.Max(.1,frequency)/HeadModel.C,x=k*radius;
        int order=(int)Math.Ceiling(x+4*Math.Cbrt(x)+12);
        var surface=Hankel(x,order+1);var near=Hankel(k*distance,order);var reference=Hankel(k*ReferenceDistance,order);
        Complex sn=0,sr=0;double prev=1,poly=cosine;
        for(int n=0;n<=order;n++)
        {
            double p=n==0?1:n==1?cosine:((2*n-1)*cosine*poly-(n-1)*prev)/n;
            if(n>=2){prev=poly;poly=p;}
            Complex derivative=n*surface[n]/x-surface[n+1];
            Complex w=(2*n+1)*p/derivative;sn+=near[n]*w;sr+=reference[n]*w;
        }
        return sn/sr*(distance/ReferenceDistance)*Complex.FromPolarCoordinates(1,k*(distance-ReferenceDistance));
    }
    static Complex[] Hankel(double x,int order)
    {
        var h=new Complex[order+1];h[0]=new(Math.Sin(x)/x,Math.Cos(x)/x);
        if(order>0)h[1]=new(Math.Sin(x)/(x*x)-Math.Cos(x)/x,Math.Cos(x)/(x*x)+Math.Sin(x)/x);
        for(int n=1;n<order;n++)h[n+1]=(2*n+1)/x*h[n]-h[n-1];return h;
    }
}
public static class SpeakerPlayback
{
    public static Project FreeFieldProject(SpeakerProject p)
    {
        var q=ProjectIO.Clone(p.Field);q.Name="自由场 / Free field";q.TemplateName="自由场";
        q.Sources=[];q.TemplateSources=[];q.ReflectionEnergyPercent=0;q.OutputDb=0;
        q.HeadModel=HeadModelKind.Fabian;q.Direct.Enabled=true;q.Direct.AirAbsorption=p.AirAbsorption;
        q.Direct.AirAbsorptionDistance=p.LeftSpeaker.Distance;
        q.Direct.Angle=Math.Abs(p.LeftSpeaker.Azimuth);q.Direct.Elevation=p.LeftSpeaker.Elevation;
        q.StrictMirror=p.LeftSpeaker.Azimuth==-p.RightSpeaker.Azimuth&&p.LeftSpeaker.Elevation==p.RightSpeaker.Elevation&&p.LeftSpeaker.Distance==p.RightSpeaker.Distance;
        return q;
    }
    // The playback reference passes through the SAME calibration/EQ/output synthesis as
    // headphone free field. At the reference geometry it is sample-identical to that preset.
    public static GenerationResult Calibrated(SpeakerProject p,CancellationToken ct=default)
    {
        var q=FreeFieldProject(p);
        if(q.StrictMirror&&p.LeftSpeaker.Azimuth<=0)
            return Generator.Generate(q,null,ct);
        var paths=Build(p,ct,q.FabianCtfCompensation);
        var head=new HeadRenderer(q);
        var inputs=new[]{p.LeftSpeaker,p.RightSpeaker}.Select(s=>new HeadReferenceInput(head.At(s.Azimuth,s.Elevation,0).Impulse,head.At(s.Azimuth,s.Elevation,1).Impulse)).ToArray();
        return Generator.Generate(q,null,ct,new(paths,Generator.FirstPeak(paths[0]),inputs));
    }
    public static double[] Air(SpeakerProject p,double distance)
    {
        var q=new Project{SampleRate=p.Field.SampleRate};q.Direct.AirAbsorption=p.AirAbsorption;q.Direct.AirAbsorptionDistance=distance;return HeadModel.Air(q);
    }
    // Far-field head response: no sphere range correction.
    // Head-centre propagation common to both speakers is removed; relative travel remains.
    // Magnitudes use a 1 m source reference. Both ears share exactly the same time origin.
    public static double[][] Build(SpeakerProject p,CancellationToken ct=default,bool ctf=false)
    {
        int sr=p.Field.SampleRate,n=Dsp.Pow2(sr*2);var pos=new[]{p.LeftSpeaker,p.RightSpeaker};
        double nearest=pos.Min(s=>s.Distance);var paths=new double[4][];
        var head=new HeadRenderer(new Project{SampleRate=sr,HeadModel=HeadModelKind.Fabian,FabianCtfCompensation=ctf});
        for(int speaker=0;speaker<2;speaker++)for(int ear=0;ear<2;ear++)
        {
            ct.ThrowIfCancellationRequested();var s=pos[speaker];var q=head.At(s.Azimuth,s.Elevation,ear);
            var spectrum=Dsp.Spectrum(q.Impulse,n);var air=Dsp.Spectrum(Air(p,s.Distance),n);
            // Remove only the tracked resampler delay; retain FABIAN's common measurement lead.
            // Shared 2 ms lead keeps the export time origin independent of distance.
            double delay=.002+(s.Distance-nearest)/HeadModel.C+q.Delay;
            for(int k=0;k<=n/2;k++)
            {
                if(k%1024==0)ct.ThrowIfCancellationRequested();double f=k*(double)sr/n;
                spectrum[k]*=air[k]/s.Distance*Complex.FromPolarCoordinates(1,-2*Math.PI*f*delay);
                if(k==0||k==n/2)spectrum[k]=spectrum[k].Real;else spectrum[n-k]=Complex.Conjugate(spectrum[k]);
            }
            Dsp.Fft(spectrum,true);int length=(int)Math.Ceiling((.12+(s.Distance-nearest)/HeadModel.C)*sr);
            var h=new double[length];for(int i=0;i<length;i++)h[i]=spectrum[i].Real*(i<length-sr*.01?1:.5-.5*Math.Cos(Math.PI*(length-1-i)/(sr*.01)));
            paths[ear*2+speaker]=h;
        }
        return paths;
    }
}
public sealed record InverseResult(double[][] Kernels,int Delay,double ProjectionErrorDb)
{ public InverseResponse? Response {get;init;} }
public sealed record InverseResponse(double[] Frequencies,double[][] MagnitudeDb,double[][] PhaseDegrees,double[][] GroupDelayMs);
public static class SpeakerInverse
{
    // Tikhonov inverse. lambda=1/(4 g^2) bounds each singular-value gain by g.
    public static Complex[] Solve(Complex a,Complex b,Complex c,Complex d,double maxGain,double notchRegularization=0)
    {
        double lambda=Math.Max(1/(4*maxGain*maxGain),notchRegularization),u=Dsp.Power(a)+Dsp.Power(c)+lambda,v=Dsp.Power(b)+Dsp.Power(d)+lambda;
        Complex z=Complex.Conjugate(a)*b+Complex.Conjugate(c)*d;double det=u*v-Dsp.Power(z);
        return [(v*Complex.Conjugate(a)-z*Complex.Conjugate(b))/det,(v*Complex.Conjugate(c)-z*Complex.Conjugate(d))/det,
            (u*Complex.Conjugate(b)-Complex.Conjugate(z)*Complex.Conjugate(a))/det,(u*Complex.Conjugate(d)-Complex.Conjugate(z)*Complex.Conjugate(c))/det];
    }
    public static InverseResult Design(double[][] playback,int sr,double maxDb,CancellationToken ct=default,bool suppressNarrowNotches=true)
    {
        int n=Dsp.Pow2(Math.Max(sr*2,playback.Max(h=>h.Length)*4));var g=playback.Select(h=>Dsp.Spectrum(h,n)).ToArray();
        var spectra=Enumerable.Range(0,4).Select(_=>new Complex[n]).ToArray();double gain=Math.Pow(10,maxDb/20);
        var weakest=new double[n/2+1];var frequencies=new double[n/2+1];
        for(int k=0;k<weakest.Length;k++)
        {
            double u=Dsp.Power(g[0][k])+Dsp.Power(g[2][k]),v=Dsp.Power(g[1][k])+Dsp.Power(g[3][k]);
            var cross=Complex.Conjugate(g[0][k])*g[1][k]+Complex.Conjugate(g[2][k])*g[3][k];
            double strongest=(u+v+Math.Sqrt((u-v)*(u-v)+4*Dsp.Power(cross)))*.5;
            weakest[k]=Dsp.Power(g[0][k]*g[3][k]-g[1][k]*g[2][k])/Math.Max(1e-30,strongest);
            frequencies[k]=k*(double)sr/n;
        }
        // A 1/6-octave local power reference prevents narrow singular-value dips from
        // turning into inverse resonances. Matrix phases/singular vectors remain intact.
        var localPower=Dsp.SmoothPower(frequencies,weakest,6);
        for(int k=0;k<=n/2;k++)
        {
            if(k%2048==0)ct.ThrowIfCancellationRequested();var q=Solve(g[0][k],g[1][k],g[2][k],g[3][k],gain,suppressNarrowNotches?Math.Max(0,localPower[k]-weakest[k]):0);
            for(int c=0;c<4;c++){spectra[c][k]=k==0||k==n/2?q[c].Real:q[c];if(k>0&&k<n/2)spectra[c][n-k]=Complex.Conjugate(q[c]);}
        }
        foreach(var h in spectra)Dsp.Fft(h,true);
        double total=spectra.Sum(h=>h.Sum(z=>Dsp.Power(z)));int delay=128,length=n/2;double error=1;
        double[][] result=[];
        // Choose a shared modelling delay from actual discarded energy, not target reverb length.
        for(;delay<=n/4;delay*=2)
        {
            result=Enumerable.Range(0,4).Select(_=>new double[length]).ToArray();double omitted=0;
            for(int c=0;c<4;c++)for(int i=0;i<n;i++)
            {
                int t=(i+delay)%n;double w=t>=length?0:t<32?.5-.5*Math.Cos(Math.PI*t/32):t>length-256?.5-.5*Math.Cos(Math.PI*(length-1-t)/255):1;
                if(t<length)result[c][t]=spectra[c][i].Real*w;
                omitted+=Dsp.Power(spectra[c][i])*(1-w)*(1-w);
            }
            error=omitted/Math.Max(1e-30,total);if(error<1e-7||delay==n/4)break;
        }
                double tail=0;int last=length-1;
        while(last>delay+256)
        {
            double e=result.Sum(h=>h[last]*h[last]);if(tail+e>total*1e-10)break;tail+=e;last--;
        }
        int retained=Math.Min(length,last+257);
        for(int c=0;c<4;c++)
        {
            Array.Resize(ref result[c],retained);
            for(int i=Math.Max(0,retained-256);i<retained;i++)result[c][i]*=.5-.5*Math.Cos(Math.PI*(retained-1-i)/255);
        }
        double discarded=0;
        for(int c=0;c<4;c++)for(int i=0;i<n;i++)
        {int t=(i+delay)%n;double actual=t<retained?result[c][t]:0;discarded+=Math.Pow(spectra[c][i].Real-actual,2);}
        return new(result,delay,10*Math.Log10(Math.Max(1e-30,discarded/Math.Max(1e-30,total))));
    }
    public static double[][] Multiply(double[][] a,double[][] b,CancellationToken ct=default)
    {
        var output=new double[4][];for(int row=0;row<2;row++)for(int col=0;col<2;col++)
        {ct.ThrowIfCancellationRequested();output[row*2+col]=Dsp.Sum(Dsp.Convolve(a[row*2],b[col]),Dsp.Convolve(a[row*2+1],b[2+col]));}return output;
    }
}
