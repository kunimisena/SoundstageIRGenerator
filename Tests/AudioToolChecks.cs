using System.Security.Cryptography;
using SoundstageIR.Core;
public static class AudioToolChecks
{
    public static async Task Run(Action<bool,string> check,string folder,string? ffmpeg)
    {
        folder=Path.GetFullPath(folder);
        string isolated=Path.Combine(folder,"路径 含空格");Directory.CreateDirectory(isolated);
        string fake=Path.Combine(isolated,"ffmpeg.exe");File.WriteAllText(fake,"This is a test file, not an executable.");
        bool rejected=false;
        try{AudioRenderer.ResolveTools(fake);}catch(FileNotFoundException ex){rejected=ex.FileName==Path.Combine(isolated,"ffprobe.exe");}
        check(rejected,"Missing sibling ffprobe rejected without PATH fallback");
        rejected=false;try{AudioRenderer.ResolveTools(Path.Combine(isolated,"missing.exe"));}catch(FileNotFoundException){rejected=true;}
        check(rejected,"Invalid explicit path rejected even if automatic tools exist");
        string wrong=Path.Combine(isolated,"wrong.exe");File.WriteAllText(wrong,"");
        rejected=false;try{AudioRenderer.ResolveTools(wrong);}catch(ArgumentException){rejected=true;}
        check(rejected,"Other executable name rejected");
        var tools=AudioRenderer.ResolveTools(ffmpeg);
        check(Path.GetDirectoryName(tools.Ffmpeg)==Path.GetDirectoryName(tools.Ffprobe),"Tools resolved as a same-directory pair");
        check((await AudioRenderer.CheckToolsAsync(tools)).StartsWith("ffmpeg version"),"Actual selected tool passes versions, convolution and filter checks");
        using var cancel=new CancellationTokenSource();cancel.Cancel();rejected=false;
        try{await AudioRenderer.CheckToolsAsync(tools,cancel.Token);}catch(OperationCanceledException){rejected=true;}
        check(rejected,"Tool check respects cancellation");
        const int sr=48000,n=96000;
        double[][] signal=[new double[n],new double[n]];
        for(int i=0;i<n;i++){signal[0][i]=(float)(.12*Math.Sin(2*Math.PI*410*i/sr));signal[1][i]=(float)(.08*Math.Sin(2*Math.PI*720*i/sr));}
        string input=Path.Combine(folder,"歌曲 输入.wav");Exporter.WriteWave(input,signal,sr);
        var hash=SHA256.HashData(File.ReadAllBytes(input));
        double[][] paths=[[.8,0,.02,0],[0,.13,0,0],[0,0,.17,0],[.7,0,0,.04]];
        var result=new GenerationResult{Project=new(){Name="工具测试",SampleRate=sr},Raw=paths,Kernels=paths,EarEq=[[1.0],[1.0]],SecondEq=[1.0],Bandpass=[1.0],Contributions=[],DirectPaths=paths};
        var expected=new[]{Dsp.Sum(Dsp.Convolve(signal[0],paths[0]),Dsp.Convolve(signal[1],paths[1])),Dsp.Sum(Dsp.Convolve(signal[0],paths[2]),Dsp.Convolve(signal[1],paths[3]))};
        double maxError=0;
        foreach(var pair in new[]{(AudioFormat.Wav,AudioLevelMode.Bypass),(AudioFormat.Flac,AudioLevelMode.NormalizeLoudness),(AudioFormat.Aac,AudioLevelMode.LimitOnly)})
        {
            var rendered=await AudioRenderer.RenderAsync(result,new(input,folder,pair.Item1,pair.Item2,-18,tools.Ffmpeg));
            check(File.Exists(rendered.File)&&new FileInfo(rendered.File).Length>0,"Actual selected-tool output: "+pair);
            check(rendered.InputSamples==n&&rendered.OutputSamples==n+3,"Full convolution tail retained: "+pair);
            check(rendered.After?.IntegratedLufs is double l&&double.IsFinite(l),"Encoded file loudness measured: "+pair);
            check(!Directory.GetFiles(rendered.Folder,"work-*").Any(),"Working audio cleaned: "+pair);
            if(pair.Item2==AudioLevelMode.Bypass)
            {
                var output=Exporter.ReadWave(rendered.File);
                maxError=output.Channels.Zip(expected).Max(c=>c.First.Zip(c.Second).Max(v=>Math.Abs(v.First-v.Second)));
                check(maxError<2e-6&&rendered.GainDb==0,"WAV matches independent four-route convolution without gain changes");
            }
            else check(rendered.After?.TruePeakDbTp<=0,"Mastered output peak verified: "+pair);
            if(pair.Item2==AudioLevelMode.LimitOnly)check(rendered.GainDb==0&&rendered.SafetyGainDb==0,"Limit-only keeps convolution gain");
            var config=File.ReadAllText(Path.Combine(rendered.Folder,"project.json"));
            check(!config.Contains(tools.Ffmpeg)&&!config.Contains("FfmpegPath"),"Machine-local tool path absent from shared project");
        }
        check(SHA256.HashData(File.ReadAllBytes(input)).SequenceEqual(hash),"Input audio byte-identical after all modes");
        File.WriteAllText(Path.Combine(folder,"audio-tools-evidence.txt"),$"Explicit tool: {tools.Ffmpeg}\nIndependent convolution maximum sample error: {maxError:R}\n");
    }
}
