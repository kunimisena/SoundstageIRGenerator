using SoundstageIR.Core;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
public static class LimitOnlyChecks
{
    public static async Task Run(Action<bool,string> check,string folder)
    {
        Directory.CreateDirectory(folder);string outputs=Path.Combine(folder,"limit-only");
        var rows=new List<string>{"Case,GainDb,SafetyGainDb,LimiterCeilingDb,BeforeLUFS,AfterLUFS,AfterTruePeakDbTp"};
        GenerationResult Matrix(int sr,double gain)=>new(){Project=new(){SampleRate=sr},Raw=[[gain],[0.0],[0.0],[gain]],Kernels=[[gain],[0.0],[0.0],[gain]],EarEq=[[1.0],[1.0]],SecondEq=[1.0],Bandpass=[1.0],Contributions=[],DirectPaths=[[gain],[0.0],[0.0],[gain]]};
        double[][] Signal(int sr)
        {
            var left=Enumerable.Range(0,sr*4).Select(i=>(double)(float)(.1*Math.Sin(2*Math.PI*997*i/sr))).ToArray();
            return [left,left.Select(v=>(double)(float)(v*.37)).ToArray()];
        }
        async Task<AudioRenderResult> Render(string name,double[][] signal,int sr,double gain=1,AudioFormat format=AudioFormat.Wav)
        {
            string input=Path.Combine(folder,name+".wav");Exporter.WriteWave(input,signal,sr);
            var hash=SHA256.HashData(File.ReadAllBytes(input));
            // An invalid normalization target must not affect the limit-only path.
            var result=await AudioRenderer.RenderAsync(Matrix(sr,gain),new(input,outputs,format,AudioLevelMode.LimitOnly,double.NaN));
            check(result.Mode==AudioLevelMode.LimitOnly&&result.GainDb==0&&result.SafetyGainDb==0&&result.TargetLufs==null,"No whole-song gain or LUFS target: "+name);
            check(SHA256.HashData(File.ReadAllBytes(input)).SequenceEqual(hash),"Input untouched: "+name);
            using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Folder,"render.json")));
            check(report.RootElement.GetProperty("mode").GetString()=="LimitOnly","Export records selected branch: "+name);
            rows.Add(string.Join(",",name,result.GainDb,result.SafetyGainDb,result.LimiterCeilingDb,result.Before?.IntegratedLufs,result.After?.IntegratedLufs,result.After?.TruePeakDbTp));
            File.WriteAllLines(Path.Combine(folder,"limit-only-measurements.csv"),rows);
            return result;
        }
        foreach(int sr in new[]{44100,48000,96000})
        {
            var signal=Signal(sr);var result=await Render("quiet-"+sr,signal,sr);
            var actual=Exporter.ReadWave(result.File).Channels;
            double error=0,energy=0;
            for(int i=2048;i<signal[0].Length-2048;i++){error+=Math.Pow(actual[0][i]-signal[0][i],2);energy+=signal[0][i]*signal[0][i];}
            check(Math.Sqrt(error/energy)<.001,"Sub-ceiling gain and timing retained: "+sr);
            check(Math.Abs(result.After!.IntegratedLufs!.Value-result.Before!.IntegratedLufs!.Value)<.05,"No loudness normalization: "+sr);
            check(actual[0].Length==signal[0].Length&&result.After.TruePeakDbTp<=0,"Duration and ceiling: "+sr);
        }
        var transients=Signal(48000);
        foreach(int frame in new[]{0,48000*2,48000*4-1}){transients[0][frame]=.95;transients[1][frame]=(float)(.95*.37);}
        // Input is legal full-scale, but the convolution doubles it and exceeds the ceiling.
        foreach(var format in new[]{AudioFormat.Wav,AudioFormat.Flac,AudioFormat.Aac})
        {
            var result=await Render("peaks-"+format,transients,48000,2,format);
            string wav=result.File;
            if(format!=AudioFormat.Wav)
            {
                wav=Path.Combine(result.Folder,"validation.wav");
                var start=new ProcessStartInfo(AudioRenderer.FindTool("ffmpeg")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
                foreach(string a in new[]{"-v","error","-y","-i",result.File,"-c:a","pcm_f32le",wav})start.ArgumentList.Add(a);
                using var process=Process.Start(start)!;string error=await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
                check(process.ExitCode==0,"Decode "+format+": "+error);
            }
            var actual=Exporter.ReadWave(wav).Channels;
            check(result.PeakBefore>1&&result.After!.TruePeakDbTp<=0&&actual.All(c=>c.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1)),"Convolution overshoot limited and encoded output verified: "+format);
            if(format!=AudioFormat.Aac)
            {
                double diff=0,energy=0;
                for(int i=48000;i<72000;i++){double expected=2*transients[0][i];diff+=Math.Pow(actual[0][i]-expected,2);energy+=expected*expected;}
                check(Math.Sqrt(diff/energy)<.001,"Limiter/codec corrections preserve quiet passages at unity gain: "+format);
                check(actual[0].Zip(actual[1]).Max(v=>Math.Abs(v.Second-.37*v.First))<2e-6,"Stereo linked limiting: "+format);
                check(actual[0].Length==transients[0].Length&&Math.Abs(actual[0][0])>.05&&Math.Abs(actual[0][^1])>.05,"Latency compensation retains first and last transients: "+format);
            }
            check(Math.Abs(actual[0][96000])<1.5,"Peak reduced instead of hidden full-song attenuation: "+format);
        }
        var silent=await Render("silence",[new double[48000],new double[48000]],48000);
        check(Exporter.ReadWave(silent.File).Channels.All(c=>c.All(v=>v==0)),"Limit-only silence stays zero");
        check(!Directory.GetFiles(outputs,"work-*",SearchOption.AllDirectories).Any(),"Limit-only owned intermediate files removed");
    }
}
