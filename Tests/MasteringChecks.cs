using StatisticalField.Core;
using System.Diagnostics;
using System.Security.Cryptography;
public static class MasteringChecks
{
    public static async Task Run(Action<bool,string> check,string folder)
    {
        Directory.CreateDirectory(folder);string outputs=Path.Combine(folder,"mastered");var rows=new List<string>{"Case,BeforeLUFS,TargetLUFS,GainDb,SafetyGainDb,AfterLUFS,AfterTruePeakDbTp"};
        GenerationResult Identity(int rate)=>new(){Project=new(){SampleRate=rate},Raw=[[1.0],[0.0],[0.0],[1.0]],Kernels=[[1.0],[0.0],[0.0],[1.0]],EarEq=[[1.0],[1.0]],SecondEq=[1.0],Bandpass=[1.0],Contributions=[],DirectPaths=[[1.0],[0.0],[0.0],[1.0]]};
        double[][] Tone(int rate,double level,int seconds=4)
        {
            var left=Enumerable.Range(0,rate*seconds).Select(i=>(double)(float)(level*Math.Sin(2*Math.PI*997*i/rate))).ToArray();
            return [left,left.Select(x=>(double)(float)(x*.37)).ToArray()];
        }
        async Task<AudioRenderResult> Render(string name,double[][] signal,int rate,AudioFormat format=AudioFormat.Wav,double target=-18)
        {
            string input=Path.Combine(folder,name+".wav");Exporter.WriteWave(input,signal,rate);
            var beforeHash=SHA256.HashData(File.ReadAllBytes(input));
            var output=await AudioRenderer.RenderAsync(Identity(rate),new(input,outputs,format,AudioLevelMode.NormalizeLoudness,target));
            check(SHA256.HashData(File.ReadAllBytes(input)).SequenceEqual(beforeHash),"Mastering preserves input "+name);
            rows.Add(string.Join(",",name,output.Before?.IntegratedLufs,target,output.GainDb,output.SafetyGainDb,output.After?.IntegratedLufs,output.After?.TruePeakDbTp));
            File.WriteAllLines(Path.Combine(folder,"mastering-measurements.csv"),rows);
            return output;
        }
        foreach(int sr in new[]{44100,48000,96000})
        {
            var signal=Tone(sr,.015);var result=await Render("quiet-"+sr,signal,sr);
            check(result.GainDb>0&&Math.Abs(result.After!.IntegratedLufs!.Value+18)<.15,"Quiet audio raised to -18 LUFS "+sr);
            check(Math.Abs(result.GainDb-(-18-result.Before!.IntegratedLufs!.Value))<1e-9,"Gain derives from full-program integrated loudness "+sr);
            var wave=Exporter.ReadWave(result.File);
            check(wave.Channels[0].Length==signal[0].Length&&wave.SampleRate==sr,"Lookahead compensated and duration exact "+sr);
            double factor=Math.Pow(10,(result.GainDb+result.SafetyGainDb)/20);
            double diff=0,energy=0;
            for(int i=2048;i<signal[0].Length-2048;i++){double expected=signal[0][i]*factor;diff+=Math.Pow(wave.Channels[0][i]-expected,2);energy+=expected*expected;}
            check(Math.Sqrt(diff/energy)<.001,"Sub-ceiling audio retains static gain and timing "+sr);
            check(result.After!.TruePeakDbTp<0,"Measured final true peak below ceiling "+sr);
        }
        var loud=await Render("loud",Tone(48000,.7),48000);check(loud.GainDb<0&&Math.Abs(loud.After!.IntegratedLufs!.Value+18)<.15,"Loud audio reduced to same loudness target");
        var alternate=await Render("target-23",Tone(48000,.02),48000,AudioFormat.Wav,-23);check(Math.Abs(alternate.After!.IntegratedLufs!.Value+23)<.15,"User-selected -23 LUFS target actually applied");
        var transient=Tone(48000,.006,6);
        foreach(int frame in new[]{0,48000*3,48000*6-1}){transient[0][frame]=.95;transient[1][frame]=(float)(.95*.37);}
        var limited=await Render("strong-transients",transient,48000);
        var limitedWave=Exporter.ReadWave(limited.File).Channels;
        check(limited.Before!.TruePeakDbTp+limited.GainDb>10,"Transient signal requires substantial limiting after loudness gain");
        check(limitedWave.All(c=>c.All(x=>double.IsFinite(x)&&Math.Abs(x)<=1))&&limited.After!.TruePeakDbTp<=0,"Limiter bounds samples and independently measured true peak");
        int steady=48000+12,burst=3*48000;
        double gainSteady=Math.Abs(limitedWave[0][steady]/transient[0][steady]),gainBurst=Math.Abs(limitedWave[0][burst]/transient[0][burst]);
        check(gainSteady>gainBurst*3,"Limiter dynamically reduces peaks, not just whole-song attenuation");
        check(limitedWave[0].Zip(limitedWave[1]).Max(v=>Math.Abs(v.Second-.37*v.First))<2e-6,"Linked limiter preserves stereo amplitude ratio");
        check(Math.Abs(limitedWave[0][^1])>.05&&Math.Abs(limitedWave[0][0])>.05,"Lookahead flush retains first and last transients");
        foreach(var format in new[]{AudioFormat.Flac,AudioFormat.Aac})
        {
            var encoded=await Render("transients-"+format,transient,48000,format);
            check(encoded.After!.TruePeakDbTp<=0&&encoded.After.IntegratedLufs is double,"Encoded output rescanned for loudness and peak "+format);
            check(encoded.SafetyGainDb<=0,"Codec safety correction reported "+format);
            var start=new ProcessStartInfo(AudioRenderer.FindTool("ffmpeg")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
            string decoded=Path.Combine(encoded.Folder,"validation.wav");
            foreach(var a in new[]{"-v","error","-y","-i",encoded.File,"-c:a","pcm_f32le",decoded})start.ArgumentList.Add(a);
            using var process=Process.Start(start)!;string error=await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();check(process.ExitCode==0,"Decode mastered "+format+" "+error);
            check(Exporter.ReadWave(decoded).Channels.All(c=>c.All(x=>double.IsFinite(x)&&Math.Abs(x)<=1)),"Decoded actual sample peaks below 0 dBFS "+format);
        }
        var silent=await Render("silence",[new double[48000],new double[48000]],48000);
        check(silent.GainDb==0&&silent.After!.IntegratedLufs==null&&Exporter.ReadWave(silent.File).Channels.All(c=>c.All(x=>x==0)),"Silence stays zero without infinite gain or invalid JSON");
        var shortSignal=await Render("short",[new double[]{.2,0,.1},new double[]{.1,0,.05}],48000);
        check(shortSignal.GainDb==0&&shortSignal.Note.Length>0&&shortSignal.OutputSamples==3&&Exporter.ReadWave(shortSignal.File).Channels[0].Any(x=>x!=0),"Short unmeasurable signal handled without invented loudness gain");
        var cancel=new CancellationTokenSource();var progress=new CancelDuringScan(cancel);bool cancelled=false;
        try{await AudioRenderer.RenderAsync(Identity(48000),new(Path.Combine(folder,"strong-transients.wav"),outputs),progress,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
        check(cancelled&&!Directory.GetFiles(outputs,"work-*",SearchOption.AllDirectories).Any(),"Cancellation during loudness scan cleans owned files");
        File.WriteAllLines(Path.Combine(folder,"mastering-measurements.csv"),rows);
    }
    sealed class CancelDuringScan(CancellationTokenSource source):IProgress<(double Fraction,string Message)>
    {public void Report((double Fraction,string Message) value){if(value.Fraction>=.55)source.Cancel();}}
}
