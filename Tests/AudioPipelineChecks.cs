using System.Diagnostics;
using System.Security.Cryptography;
using SoundstageIR.Core;
public static class AudioPipelineChecks
{
    public static async Task Run(Action<bool,string> check,string folder)
    {
        var e=new Excitation{FirstReflectionExtraPath=.5,MixingPath=12,Envelope=[new(-1,-3),new(-.7,0),new(0,-4),new(.2,-14),new(1,-60)]};
        e.Validate();check(e.Envelope[1].Y>e.Envelope[0].Y,"Full envelope permits rising early energy independently of density");
        foreach(double u in new[]{-1.0,-.8,-.2,0,.3,1})check(Math.Abs(e.EnvelopeCoordinate(e.EnvelopeDistance(u))-u)<1e-12,"Distance envelope roundtrip "+u);
        var p=Presets.BuiltIn[0].Create();p.Sources[0].Left=e;
        check(ProjectIO.Serialize(ProjectIO.Deserialize(ProjectIO.Serialize(p)))==ProjectIO.Serialize(p),"Full envelope JSON exact roundtrip");
        check(Presets.BuiltIn.Single(x=>x.Name=="柔和音乐厅").WetPercent==65&&Presets.BuiltIn.Single(x=>x.Name=="悠长大厅").WetPercent==80,"Hall defaults permit reverberant energy to dominate");
        check(NoiseKernel.EnvelopeDb(e,-1)==-3&&NoiseKernel.EnvelopeDb(e,0)==-4,"First reflection can start strong while energy falls during mixing");
        var zero=e.Clone();zero.MixingPath=0;var zeroKernel=NoiseKernel.Generate(zero,48000,42,Guid.Empty,0);
        check(zeroKernel.All(double.IsFinite),"Zero mixing distance is finite");
        var actual=Generator.Generate(Presets.BuiltIn[0].Create());
        string apo=ApoExporter.Export(actual,Path.Combine(folder,"portable host 带空格"));
        var config=File.ReadAllText(TestFiles.Get(apo,"APO.txt"));
        check(config.Contains("Copy: LL=L LR=L RL=R RR=R")&&config.Contains("Copy: L=LL+RL R=LR+RR"),"APO replaces L/R with correct four-path matrix");
        foreach(int route in new[]{0,1,2,3})
        {
            string wav=TestFiles.Get(apo,Generator.RouteNames[route]+".wav");
            check(config.Contains("Convolution: "+wav)&&Exporter.ReadWave(wav).Channels[0].SequenceEqual(actual.Kernels[route]),"APO absolute UTF-8 path and exact IR "+route);
        }
        // Reference convolution uses direct independent matrix evaluation, not the renderer's graph.
        const int sr=48000,n=25017,taps=131;
        var random=new Random(219);double[][] signal=[new double[n],new double[n]];
        for(int i=0;i<n;i++){signal[0][i]=(float)((random.NextDouble()-.5)*.25);signal[1][i]=(float)((random.NextDouble()-.5)*.19);}
        string input=Path.Combine(folder,"立体声 输入.wav");Exporter.WriteWave(input,signal,sr);var hash=SHA256.HashData(File.ReadAllBytes(input));
        double[][] paths=Enumerable.Range(0,4).Select(_=>new double[taps]).ToArray();
        paths[0][0]=.8;paths[0][63]=.02;paths[1][17]=.25;paths[2][3]=-.15;paths[3][0]=.7;paths[3][130]=.03;
        var r=Synthetic(paths,sr);string outputs=Path.Combine(folder,"audio");
        var rendered=await AudioRenderer.RenderAsync(r,new(input,outputs,AudioFormat.Wav,AudioLevelMode.Bypass));
        var wave=Exporter.ReadWave(rendered.File);
        var expected=new[]{Dsp.Sum(Dsp.Convolve(signal[0],paths[0]),Dsp.Convolve(signal[1],paths[1])),Dsp.Sum(Dsp.Convolve(signal[0],paths[2]),Dsp.Convolve(signal[1],paths[3]))};
        double error=wave.Channels.Zip(expected).Max(c=>c.First.Zip(c.Second).Max(x=>Math.Abs(x.First-x.Second)));
        check(error<2e-6,"Offline matrix matches independent convolution, max error "+error.ToString("G6"));
        check(wave.Channels[0].Length==n+taps-1&&rendered.GainDb==0,"Exact output length, flushed tail, no hidden gain");
        check(SHA256.HashData(File.ReadAllBytes(input)).SequenceEqual(hash),"Source audio remains byte-identical");
        check(!Directory.GetFiles(rendered.Folder,"work-*").Any(),"Render working audio cleaned after success");
        string mono=Path.Combine(folder,"单声道.wav");Exporter.WriteWave(mono,[signal[0]],44100);
        var monoRender=await AudioRenderer.RenderAsync(r,new(mono,outputs,AudioFormat.Wav,AudioLevelMode.Bypass));
        check(monoRender.SampleRate==sr&&Math.Abs(monoRender.InputSamples-n*sr/44100.0)<=2,"Mono input resampled to current kernel rate");
        foreach(int rate in new[]{44100,96000})
        {
            var other=await AudioRenderer.RenderAsync(Synthetic(paths,rate),new(input,outputs,AudioFormat.Wav,AudioLevelMode.Bypass));
            check(other.SampleRate==rate&&Math.Abs(other.InputSamples-n*(double)rate/sr)<=2,"Offline render at "+rate);
        }
        double[][] loud=signal.Select(ch=>ch.Select(v=>(double)(float)(v*20)).ToArray()).ToArray();string loudInput=Path.Combine(folder,"loud.wav");Exporter.WriteWave(loudInput,loud,sr);
        var protectedRender=await AudioRenderer.RenderAsync(r,new(loudInput,outputs,AudioFormat.Wav,AudioLevelMode.NormalizeLoudness));
        double peak=Exporter.ReadWave(protectedRender.File).Channels.Max(c=>c.Max(v=>Math.Abs(v)));
        check(protectedRender.GainDb<0&&peak<=1&&protectedRender.After!.TruePeakDbTp<=0,"Loudness compensation lowers loud input and final peak is bounded");
        foreach(var fmt in new[]{AudioFormat.Flac,AudioFormat.Aac})
        {
            var compressed=await AudioRenderer.RenderAsync(r,new(input,outputs,fmt,AudioLevelMode.Bypass));
            string decoded=Path.Combine(compressed.Folder,"decoded-validation.wav");
            var si=new ProcessStartInfo(AudioRenderer.FindTool("ffmpeg")){CreateNoWindow=true,UseShellExecute=false,RedirectStandardError=true};
            foreach(var a in new[]{"-v","error","-y","-i",compressed.File,"-c:a","pcm_f32le",decoded})si.ArgumentList.Add(a);
            using var process=Process.Start(si)!;string err=await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
            check(process.ExitCode==0,"Decode rendered "+fmt+" "+err);
            var back=Exporter.ReadWave(decoded);check(back.Channels.Length==2&&back.SampleRate==sr&&back.Channels.All(c=>c.All(double.IsFinite)),"Valid stereo encoded output "+fmt);
            if(fmt==AudioFormat.Flac)check(back.Channels.Zip(wave.Channels).Max(c=>c.First.Zip(c.Second).Max(v=>Math.Abs(v.First-v.Second)))<2e-6,"FLAC preserves rendered audio to 24-bit accuracy");
        }
        var cancel=new CancellationTokenSource();cancel.Cancel();bool stopped=false;
        try{await AudioRenderer.RenderAsync(r,new(input,outputs),cancellation:cancel.Token);}catch(OperationCanceledException){stopped=true;}
        check(stopped,"Audio render pre-cancellation");
        using var activeCancel=new CancellationTokenSource();var progress=new CancelProgress(activeCancel);
        stopped=false;try{await AudioRenderer.RenderAsync(r,new(input,outputs),progress,activeCancel.Token);}catch(OperationCanceledException){stopped=true;}
        check(stopped&&!Directory.GetFiles(outputs,"work-*",SearchOption.AllDirectories).Any(),"In-flight cancellation kills worker and removes only owned temporary files");
        File.WriteAllText(Path.Combine(folder,"audio-reference.txt"),$"Matrix max absolute sample error: {error:R}\nOutput frames: {wave.Channels[0].Length}\n");
    }
    sealed class CancelProgress(CancellationTokenSource source):IProgress<(double Fraction,string Message)>
    {public void Report((double Fraction,string Message) item){if(item.Fraction>=.15)source.Cancel();}}
    static GenerationResult Synthetic(double[][] paths,int sr)=>new(){Project=new(){SampleRate=sr},Raw=paths,Kernels=paths,EarEq=[[1.0],[1.0]],SecondEq=[1.0],Bandpass=[1.0],Contributions=[],DirectPaths=paths};
}
