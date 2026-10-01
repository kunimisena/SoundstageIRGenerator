using System.Numerics;

namespace SoundstageIR.Core;

public enum HeadModelKind { Sphere, Fabian }

// FIRs retain the measured excess phase. Delay is only the explicitly tracked
// resampler lead-in (or the original sphere propagation delay), never peak alignment.
public sealed record EarTransfer(double[] Impulse, double Delay, HeadFilter? Sphere = null)
{
    public double[] Apply(double[] input, int sr) => Sphere is {} q
        ? HeadModel.Apply(input, q, sr) : Dsp.Convolve(input, Impulse);
}

public sealed class HeadRenderer(Project project)
{
    readonly Dictionary<(double Az, double El, int Ear), EarTransfer> cache = new();
    public double MinimumDelay => project.HeadModel == HeadModelKind.Sphere
        ? -project.HeadRadius / HeadModel.C : HeadBandwidthExtension.Delay(project.SampleRate);
    public EarTransfer At(double az, double el, int ear)
    {
        var key = (az, el, ear);
        if (cache.TryGetValue(key, out var result)) return result;
        if (project.HeadModel == HeadModelKind.Sphere)
        {
            var q = HeadModel.At(az, el, ear, project);
            result = new(HeadModel.Impulse(q, project.SampleRate), q.Delay, q);
        }
        else
        {
            result = FabianData.At(az, el, ear, project.SampleRate, project.FabianCtfCompensation);
        }
        cache.Add(key, result);
        return result;
    }
    public static string Description(Project p) => p.HeadModel == HeadModelKind.Sphere
        ? "球形头 · Brown–Duda 遮挡与时差"
        : "FABIAN · 耳廓与肩胸部" + (p.FabianCtfCompensation ? " · 共同频响补偿" : " · 原始 HRTF") + " · 带外边缘延伸";
}

public static class FabianData
{
    public const int NativeRate = 44100;
    const int Half = 48;
    sealed record Direction(double Az, double El, double X, double Y, double Z, double[][] H);
    sealed record Data(Direction[] Directions, double[] Ctf);
    static readonly Lazy<Data> database = new(Read);
    public static int DirectionCount => database.Value.Directions.Length;
    static Data Read()
    {
        using var stream = typeof(FabianData).Assembly.GetManifestResourceStream("SoundstageIR.Core.FABIAN.bin")
            ?? throw new InvalidDataException("缺少内置 FABIAN 数据，请重新复制完整发布程序。");
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(8)) != "SFSHRTF1") throw new InvalidDataException("FABIAN 数据格式不匹配。");
        int rate = reader.ReadInt32(), count = reader.ReadInt32(), taps = reader.ReadInt32(), ctfTaps = reader.ReadInt32();
        if (rate != NativeRate || count != 11950 || taps != 256 || ctfTaps != 256) throw new InvalidDataException("FABIAN 数据头错误。");
        double[] ReadSamples(int n) => Enumerable.Range(0,n).Select(_ => (double)reader.ReadSingle()).ToArray();
        var ctf = ReadSamples(ctfTaps); var directions = new Direction[count];
        for (int i=0; i<count; i++)
        {
            double az = reader.ReadSingle(), el = reader.ReadSingle(); var v = Unit(az,el);
            directions[i] = new(az,el,v.X,v.Y,v.Z,[ReadSamples(taps),ReadSamples(taps)]);
        }
        return new(directions,ctf);
    }
    static (double X,double Y,double Z) Unit(double az,double el)
    {
        double a=az*Math.PI/180,e=el*Math.PI/180;
        return (Math.Cos(e)*Math.Cos(a),Math.Cos(e)*Math.Sin(a),Math.Sin(e));
    }
    static Direction Nearest(double sofaAz,double el)
    {
        var v=Unit(sofaAz,el); double best=double.NegativeInfinity; Direction? result=null;
        foreach(var d in database.Value.Directions)
        {
            double dot=v.X*d.X+v.Y*d.Y+v.Z*d.Z;
            if(dot>best){best=dot;result=d;}
        }
        return result!;
    }
    public static double AngularError(double appAz,double el)
    {
        var v=Unit(Math.Abs(appAz),el); var d=Nearest(Math.Abs(appAz),el);
        return Math.Acos(Math.Clamp(v.X*d.X+v.Y*d.Y+v.Z*d.Z,-1,1))*180/Math.PI;
    }
    public static EarTransfer At(double appAz,double el,int ear,int sr,bool compensate)
        =>HeadBandwidthExtension.Apply(MeasuredAt(appAz,el,ear,NativeRate,compensate).Impulse,sr);

    // Unmodified measurement access for dataset inspection and validation.
    // The application always renders through At, including bandwidth extension.
    public static EarTransfer MeasuredAt(double appAz,double el,int ear,int sr,bool compensate)
    {
        if(sr is not (44100 or 48000 or 96000))throw new ArgumentException("FABIAN 采样率不受支持。");
        if(ear is <0 or >1)throw new ArgumentOutOfRangeException(nameof(ear));
        // Use the measured LEFT ear over the full sphere as the anatomical reference.
        // Reflect source azimuth for the right ear; never average complex responses.
        // App negative azimuth is left, whereas SOFA positive azimuth is left.
        double az=ear==0?-appAz:appAz;
        bool median=Math.Abs(Math.Sin(appAz*Math.PI/180)*Math.Cos(el*Math.PI/180))<1e-9;
        // Canonicalize the symmetry plane/poles so nearest-grid ties select one IR.
        if(median)az=Math.Abs(appAz);
        var direction=Nearest(az,el);
        var h=(double[])direction.H[0].Clone();
        if(compensate)h=Dsp.Convolve(h,database.Value.Ctf);
        return new(Resample(h,sr),ResampleDelay(sr));
    }
    public static double ResampleDelay(int sr) => sr==NativeRate?0:-Half/(double)NativeRate;
    static double[] Resample(double[] h,int rate)
    {
        if(rate==NativeRate)return h;
        // Continuous-time interpolation of an impulse response requires fs_in/fs_out
        // scaling to preserve transfer-function gain, unlike ordinary audio resampling.
        // Blackman-windowed sinc support is retained on BOTH ends; lead-in is reported
        // separately so sample-rate conversion does not alter physical path differences.
        double ratio=(double)rate/NativeRate;
        var y=new double[(int)Math.Ceiling((h.Length+2*Half)*ratio)];
        for(int n=0;n<y.Length;n++)
        {
            double t=n/ratio-Half; int center=(int)Math.Floor(t);
            for(int k=Math.Max(0,center-Half+1);k<=Math.Min(h.Length-1,center+Half);k++)
            {
                double d=t-k; if(Math.Abs(d)>=Half)continue;
                double sinc=Math.Abs(d)<1e-12?1:Math.Sin(Math.PI*d)/(Math.PI*d);
                double window=.42+.5*Math.Cos(Math.PI*d/Half)+.08*Math.Cos(2*Math.PI*d/Half);
                y[n]+=h[k]*sinc*window/ratio;
            }
        }
        return y;
    }
}
