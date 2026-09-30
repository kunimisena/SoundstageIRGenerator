using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace StatisticalField.Core;

public enum AudioFormat { Wav, Flac, Aac }
public enum AudioLevelMode { NormalizeLoudness, LimitOnly, Bypass }
public sealed record AudioRenderOptions(string Input,string OutputParent,AudioFormat Format=AudioFormat.Aac,AudioLevelMode Mode=AudioLevelMode.NormalizeLoudness,double TargetLufs=-18,string? FfmpegPath=null);
public sealed record LoudnessMeasurement(double? IntegratedLufs,double? TruePeakDbTp,double? LoudnessRangeLu);
public sealed record AudioRenderResult(string File,string Folder,long InputSamples,long OutputSamples,int SampleRate,double GainDb,double PeakBefore)
{
    public LoudnessMeasurement? Before {get;init;}
    public LoudnessMeasurement? After {get;init;}
    public double SafetyGainDb {get;init;}
    public double? TargetLufs {get;init;}
    public AudioLevelMode Mode {get;init;}
    public double? LimiterCeilingDb {get;init;}
    public string Note {get;init;}="";
}

/// <summary>Offline matrix rendering with an external FFmpeg process.</summary>
public static partial class AudioRenderer
{
    static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
    public static string FindTool(string name)
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir!=null)
        {
            string path=Path.Combine(dir.FullName,"tools","ffmpeg",name+".exe");
            if(File.Exists(path))return path;dir=dir.Parent;
        }
        string installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ffmpeg","bin",name+".exe");
        if(File.Exists(installed))return installed;
        foreach(string entry in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            try { string candidate=Path.Combine(entry.Trim().Trim('"'),name+".exe");if(File.Exists(candidate))return candidate; }
            catch(ArgumentException) { }
        }
        throw new FileNotFoundException("缺少 "+name+".exe。处理歌曲需要 FFmpeg；请在“处理歌曲”中选择 ffmpeg.exe，同目录需有 ffprobe.exe。");
    }
    public static async Task<AudioRenderResult> RenderAsync(GenerationResult result,AudioRenderOptions options,IProgress<(double Fraction,string Message)>? progress=null,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(!Enum.IsDefined(options.Mode))throw new ArgumentException("未知的歌曲电平处理模式。");
        bool normalize=options.Mode==AudioLevelMode.NormalizeLoudness, limit=options.Mode!=AudioLevelMode.Bypass;
        if(normalize)Excitation.Range(options.TargetLufs,-30,-9,"目标响度 LUFS");
        string input=Path.GetFullPath(options.Input);
        if(!File.Exists(input))throw new FileNotFoundException("找不到输入音频。",input);
        var tools=ResolveTools(options.FfmpegPath);
        await CheckToolsAsync(tools,cancellation);
        string ffmpeg=tools.Ffmpeg,ffprobe=tools.Ffprobe;
        string probe=await RunAsync(ffprobe,["-v","error","-select_streams","a:0","-show_entries","stream=channels,duration:format=duration","-of","json",input],null,cancellation);
        using var json=JsonDocument.Parse(probe);var streams=json.RootElement.GetProperty("streams");
        if(streams.GetArrayLength()==0)throw new ArgumentException("文件中没有音轨。");
        int channels=streams[0].GetProperty("channels").GetInt32();
        if(channels is <1 or >2)throw new ArgumentException("本功能接受单声道或双声道歌曲，请先将多声道素材转换成立体声。");
        double seconds=0;
        if(json.RootElement.TryGetProperty("format",out var format)&&format.TryGetProperty("duration",out var duration))double.TryParse(duration.GetString(),NumberStyles.Float,Invariant,out seconds);
        string folder=OutputNames.NewDirectory(Path.GetFullPath(options.OutputParent),result.Project);
        Directory.CreateDirectory(folder);
        string decoded=Path.Combine(folder,"work-input.f32"),rendered=Path.Combine(folder,"work-rendered.f32"),ir=Path.Combine(folder,"work-matrix.wav"),mastered=Path.Combine(folder,"work-mastered.f32");
        string suffix=options.Format switch{AudioFormat.Flac=>".flac",AudioFormat.Aac=>".m4a",_=>".wav"};
        string stem=OutputNames.Label(result.Project)+"_"+Path.GetFileNameWithoutExtension(input);if(stem.Length>90)stem=stem[..90];
        string output=Path.Combine(folder,stem+"_空间处理"+suffix),partial=Path.Combine(folder,"encoding"+suffix);
        int sr=result.Project.SampleRate;string rate=sr.ToString(Invariant);
        try
        {
            progress?.Report((0,"读取音频并匹配卷积核采样率…"));
            string decodeFilter=$"aresample={sr}:resampler=soxr:precision=28"+(channels==1?",pan=stereo|c0=c0|c1=c0":"");
            await RunAsync(ffmpeg,["-v","error","-nostdin","-y","-i",input,"-map","0:a:0","-vn","-af",decodeFilter,"-c:a","pcm_f32le","-f","f32le",decoded,"-progress","pipe:1","-nostats"],
                t=>progress?.Report((Math.Clamp(t/Math.Max(1,seconds),0,1)*.15,"读取 / 重采样…")),cancellation);
            long frames=new FileInfo(decoded).Length/8;
            if(frames==0)throw new ArgumentException("音轨为空。");
            int taps=result.Kernels.Max(k=>k.Length);long total=checked(frames+taps-1);
            Exporter.WriteWave(ir,result.Kernels,sr);
            // File channel order: L->left, R->left, L->right, R->right.
            // Disable BOTH old/new FFmpeg IR normalization controls. No extra dry mix.
            string graph=$"[0:a]pan=4c|c0=c0|c1=c1|c2=c0|c3=c1,apad=pad_len={taps-1}[paths];"+
                "[paths][1:a]afir=dry=1:wet=1:irnorm=-1:gtype=none:irlink=0:irgain=1:precision=double:maxir=60:minp=4096:maxp=16384[convolved];"+
                $"[convolved]pan=stereo|c0=c0+c1|c1=c2+c3,atrim=end_sample={total}[out]";
            await RunAsync(ffmpeg,["-v","error","-nostdin","-y","-f","f32le","-ar",rate,"-ac","2","-i",decoded,"-i",ir,"-filter_complex",graph,"-map","[out]","-c:a","pcm_f32le","-f","f32le",rendered,"-progress","pipe:1","-nostats"],
                t=>progress?.Report((.15+Math.Clamp(t/(total/(double)sr),0,1)*.35,"四路径卷积 / 保留完整尾部…")),cancellation);
            if(new FileInfo(rendered).Length!=total*8)throw new InvalidDataException("卷积输出长度不匹配，未导出不完整音频。");
            progress?.Report((.51,"扫描卷积结果响度与真峰值…"));
            double peak=Peak(rendered,cancellation);
            if(!double.IsFinite(peak))throw new InvalidDataException("输出包含非有限数值。");
            var before=peak==0?new LoudnessMeasurement(null,null,0):await MeasureAsync(ffmpeg,rendered,sr,
                t=>progress?.Report((.51+Math.Clamp(t/(total/(double)sr),0,1)*.10,"扫描响度与真峰值…")),cancellation);
            double gainDb=normalize&&before.IntegratedLufs is double measured?options.TargetLufs-measured:0;
            string audio=rendered;
            double safetyGainDb=0,limiterCeilingDb=0;
            LoudnessMeasurement? after=null;bool verified=false;
            for(int attempt=0;attempt<6;attempt++)
            {
                if(limit&&(attempt==0||!normalize))
                {
                    progress?.Report((.62,normalize?"固定响度增益 → 双声道联动 0 dB 限幅…":"保持卷积后电平 → 仅对峰值限幅…"));
                    await ApplyMasteringAsync(ffmpeg,rendered,mastered,sr,total,gainDb,
                        t=>progress?.Report((.62+Math.Clamp(t/(total/(double)sr),0,1)*.14,"双声道联动 / 超采样限幅…")),cancellation,limiterCeilingDb);
                    audio=mastered;
                    if(new FileInfo(audio).Length!=total*8)throw new InvalidDataException("限幅后的长度不匹配。");
                }
                double masterPeak=Peak(audio,cancellation);
                if(!double.IsFinite(masterPeak))throw new InvalidDataException("限幅结果包含非有限数值。");
                if(!limit&&options.Format!=AudioFormat.Wav&&masterPeak>1)
                    throw new InvalidOperationException("输出峰值超过 0 dBFS，请选择仅限幅或响度补偿后限幅，或用 float32 WAV 保留原始结果。");
                if(limit&&masterPeak>1)
                {
                    if(normalize)safetyGainDb=Math.Min(safetyGainDb,-20*Math.Log10(masterPeak)-.02);
                    else
                    {
                        // Limit-only never attenuates the whole song. Rerender from the original
                        // convolution with a lower limiter threshold to catch resampling overshoot.
                        limiterCeilingDb-=20*Math.Log10(masterPeak)+.10;
                        continue;
                    }
                }
                await EncodeAsync(ffmpeg,audio,partial,options.Format,sr,Math.Pow(10,safetyGainDb/20),
                    t=>progress?.Report((.77+Math.Clamp(t/(total/(double)sr),0,1)*.08,"编码歌曲…")),cancellation);
                progress?.Report((.86,"重新解码成品，复查响度与真峰值…"));
                after=await MeasureAsync(ffmpeg,partial,null,
                    t=>progress?.Report((.86+Math.Clamp(t/(total/(double)sr),0,1)*.12,"复查成品响度与真峰值…")),cancellation);
                if(!limit || after.TruePeakDbTp is not double tp || tp<=-.02){verified=true;break;}
                if(normalize)safetyGainDb-=Math.Max(0,tp)+.10;
                else limiterCeilingDb-=Math.Max(0,tp)+.10;
                progress?.Report((.62,normalize?"修正编码后的峰值超限，重新编码…":"收紧限幅阈值，保持其余部分电平并重新编码…"));
            }
            if(!verified)throw new InvalidDataException("编码后真峰值仍超过上限，未交付不合格文件。");
            cancellation.ThrowIfCancellationRequested();
            string note=normalize&&before.IntegratedLufs==null?"静音、片段过短或低于测量门限：未提升响度，仍执行限幅。":"";
            var report=new AudioRenderResult(output,folder,frames,total,sr,gainDb,peak)
                {Before=before,After=after,SafetyGainDb=safetyGainDb,TargetLufs=normalize?options.TargetLufs:null,Mode=options.Mode,LimiterCeilingDb=limit?limiterCeilingDb:null,Note=note};
            File.WriteAllText(Path.Combine(folder,"render.json"),ProjectIO.Serialize(new{inputName=Path.GetFileName(input),options.Format,mode=options.Mode.ToString(),normalize,report.LimiterCeilingDb,report.SampleRate,report.InputSamples,report.OutputSamples,report.TargetLufs,report.GainDb,report.SafetyGainDb,report.PeakBefore,report.Before,report.After,report.Note,kernelSamples=taps,zeroSample=result.ZeroSample,
                loudness="ITU-R BS.1770 / EBU R128 gated integrated loudness; custom -18 LUFS music default, not EBU broadcast target",
                limiter=limit?"4x oversampling, stereo linked, 0 dBFS ceiling, 5 ms lookahead / 50 ms release, latency compensated; decoded output true-peak verification; normalization uses common scalar correction, limit-only lowers limiter threshold without whole-song attenuation":"bypass",
                kernelRoutes=Generator.RouteNames}));
            ProjectIO.Save(result.Project,Path.Combine(folder,"project.json"));
            static string Db(double? value,string unit)=>value is double v?$"{v:F2} {unit}":"不可测量 / 静音";
            File.WriteAllText(Path.Combine(folder,"试听说明.txt"),$"歌曲已包含空间卷积、空间校正 EQ 和最终带通。用耳机播放时关闭重复的空间卷积，个人耳机 EQ 可照常使用。\n{sr} Hz；保留完整尾部。\n响度处理：{(normalize?"扫描 → 固定增益 → 0 dB 限幅":limit?"保持卷积后电平 → 仅限幅":"旁路")}\n处理前 {Db(before.IntegratedLufs,"LUFS")}；成品 {Db(after?.IntegratedLufs,"LUFS")}；成品真峰值 {Db(after?.TruePeakDbTp,"dBTP")}。\n响度补偿 {gainDb:F2} dB；编码/峰值额外修正 {safetyGainDb:F2} dB。\n{(limit?$"限幅器阈值 {limiterCeilingDb:F2} dBFS；成品峰值上限 0 dB。":"")}\n增益已实际写入音频，不依赖 ReplayGain 标签。仅限幅模式不施加整体增益；响度补偿模式的成品响度可能低于目标。\n{note}\n");
            File.Move(partial,output);progress?.Report((1,"音频已导出："+output));return report;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(folder,"未完成.txt"),ex is OperationCanceledException?"已取消；输入文件保持不变。":ex.Message);
            throw;
        }
        finally
        {
            // Only individually named files created by this render job are removed.
            foreach(string file in new[]{decoded,rendered,mastered,ir,partial})if(File.Exists(file))File.Delete(file);
        }
    }
    static double Peak(string path,CancellationToken ct)
    {
        using var stream=File.OpenRead(path);byte[] buffer=new byte[65536];double max=0;int read;
        while((read=stream.ReadAtLeast(buffer,buffer.Length,false))>0)
        {ct.ThrowIfCancellationRequested();foreach(float value in MemoryMarshal.Cast<byte,float>(buffer.AsSpan(0,read))){if(!float.IsFinite(value))return double.NaN;max=Math.Max(max,Math.Abs((double)value));}}
        return max;
    }
    static async Task<string> RunAsync(string executable,IEnumerable<string> arguments,Action<double>? report,CancellationToken ct,bool diagnostics=false)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new InvalidOperationException("无法启动音频工具。");
        using var registration=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){} });
        var errors=process.StandardError.ReadToEndAsync();var lines=new List<string>();
        try
        {
            while(await process.StandardOutput.ReadLineAsync(ct) is {} line)
            {
                if(report==null)lines.Add(line);
                else if(line.StartsWith("out_time_us=")&&double.TryParse(line.AsSpan(12),NumberStyles.Float,Invariant,out double microseconds))report(microseconds/1e6);
            }
            await process.WaitForExitAsync(ct);string error=await errors;ct.ThrowIfCancellationRequested();
            if(process.ExitCode!=0)throw new InvalidOperationException("音频工具处理失败："+(error.Length>3000?error[^3000..]:error));
            return diagnostics?error:string.Join("\n",lines);
        }
        catch(OperationCanceledException)
        {
            try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
            await process.WaitForExitAsync(CancellationToken.None);await errors;throw;
        }
    }
}
