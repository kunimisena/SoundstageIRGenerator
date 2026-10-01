using System.Globalization;
using System.Numerics;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
static class SpeakerFreeFieldChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        // Both references are produced by the original headphone generator, not SpeakerPlayback.
        var free=Presets.BuiltIn.Single(p=>p.Name=="自由场").Create();free.FabianCtfCompensation=false;free.Equalize=false;
        free.Direct.AirAbsorption=false;free.HeadModel=HeadModelKind.Fabian;free.StrictMirror=true;
        var reference=Generator.Generate(free);
        var targetProject=Presets.BuiltIn.Single(p=>p.Name=="紧凑监听").Create();targetProject.StrictMirror=true;
        var target=Generator.Generate(targetProject).Kernels;
        var reports=new List<string>{"# Original-generator free-field inverse comparison", "", "References: original Generator free-field raw direct paths, and its final WAV-equivalent kernels with EQ disabled. The latter retains output bandpass and common level calibration. Neither is a measured loudspeaker or listener.","", "All cases use a 12 dB inverse singular-gain setting and narrow-notch regularization. Target: original Compact monitor binaural result.","", "| Playback reference | Target reconstruction error / dB | Identity diagonal error / dB | Crosstalk / diagonal energy, 100 Hz–10 kHz / dB | Delay / ms |", "|---|---:|---:|---:|---:|"};
        foreach(var (name,g) in new[]{("Original raw direct paths",reference.DirectPaths),("Original final free-field kernels",reference.Kernels)})
        {
            var inverse=SpeakerInverse.Design(g,free.SampleRate,12);var drive=SpeakerInverse.Multiply(inverse.Kernels,target);var actual=SpeakerInverse.Multiply(g,drive);
            double error=SpeakerGenerator.RelativeError(actual,target,inverse.Delay,free.SampleRate);
            var identity=SpeakerInverse.Multiply(g,inverse.Kernels);int n=Dsp.Pow2(identity.Max(h=>h.Length));var h=identity.Select(v=>Dsp.Spectrum(v,n)).ToArray();double diagonal=0,cross=0,diagError=0;
            for(int k=(int)Math.Ceiling(100.0*n/free.SampleRate);k<=10000.0*n/free.SampleRate;k++)
            {var desired=Complex.FromPolarCoordinates(1,-2*Math.PI*k*inverse.Delay/n);diagonal+=Dsp.Power(h[0][k])+Dsp.Power(h[3][k]);cross+=Dsp.Power(h[1][k])+Dsp.Power(h[2][k]);diagError+=Dsp.Power(h[0][k]-desired)+Dsp.Power(h[3][k]-desired);}
            double crossDb=Dsp.Db(cross/diagonal),diagDb=Dsp.Db(diagError/(2*(Math.Floor(10000.0*n/free.SampleRate)-Math.Ceiling(100.0*n/free.SampleRate)+1)));
            reports.Add(FormattableString.Invariant($"| {name} | {error:F2} | {diagDb:F2} | {crossDb:F2} | {inverse.Delay*1000.0/free.SampleRate:F2} |"));
            Console.WriteLine($"{name}: reconstruction {error:F2} dB, crosstalk {crossDb:F2} dB, inverse delay {inverse.Delay*1000.0/free.SampleRate:F2} ms");
            check(actual.All(a=>a.All(double.IsFinite)),name+" finite");check(error< -10,name+" recovers broad target response");check(crossDb< -10,name+" suppresses broadband crosstalk");
        }
        var freq=Dsp.Frequencies;var power=new[]{Dsp.PowerAt(reference.DirectPaths[0],free.SampleRate),Dsp.PowerAt(reference.DirectPaths[1],free.SampleRate)};
        var broad=power.Select(v=>Dsp.SmoothPower(freq,v,3)).ToArray();
        File.WriteAllLines(Path.Combine(folder,"free-field-spectral-reference.csv"),new[]{"Hz,SameSideThirdOctDb,CrossSideThirdOctDb"}.Concat(freq.Select((f,i)=>FormattableString.Invariant($"{f:R},{Dsp.Db(broad[0][i]):R},{Dsp.Db(broad[1][i]):R}"))));
        reports.AddRange(["","The CSV contains 1/3-octave power-smoothed same-side and cross-side curves, for simple-reverb template exploration. It is a directional transmission spectrum, not a reverberation decay measurement. No preset or RT60 curve is overwritten."]);
        File.WriteAllLines(Path.Combine(folder,"free-field-comparison.md"),reports);
    }
}
