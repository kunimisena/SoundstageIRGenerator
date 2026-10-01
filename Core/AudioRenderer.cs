using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace SoundstageIR.Core;

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
        throw new FileNotFoundException(SoundstageIR.Core.TextCatalog.T("TA041D0162C")+name+SoundstageIR.Core.TextCatalog.T("T98A2812480"));
    }
    public static async Task<AudioRenderResult> RenderAsync(GenerationResult result,AudioRenderOptions options,IProgress<(double Fraction,string Message)>? progress=null,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(!Enum.IsDefined(options.Mode))throw new ArgumentException(SoundstageIR.Core.TextCatalog.T("TD12B9C224B"));
        bool normalize=options.Mode==AudioLevelMode.NormalizeLoudness, limit=options.Mode!=AudioLevelMode.Bypass;
        if(normalize)Excitation.Range(options.TargetLufs,-30,-9,SoundstageIR.Core.TextCatalog.T("T64324EBB5F"));
        string input=Path.GetFullPath(options.Input);
        if(!File.Exists(input))throw new FileNotFoundException(SoundstageIR.Core.TextCatalog.T("TE602D654C1"),input);
        var tools=ResolveTools(options.FfmpegPath);
        await CheckToolsAsync(tools,cancellation);
        string ffmpeg=tools.Ffmpeg,ffprobe=tools.Ffprobe;
        string probe=await RunAsync(ffprobe,["-v","error","-select_streams","a:0","-show_entries","stream=channels,duration:format=duration","-of","json",input],null,cancellation);
        using var json=JsonDocument.Parse(probe);var streams=json.RootElement.GetProperty("streams");
        if(streams.GetArrayLength()==0)throw new ArgumentException(SoundstageIR.Core.TextCatalog.T("T30561B7A34"));
        int channels=streams[0].GetProperty("channels").GetInt32();
        if(channels is <1 or >2)throw new ArgumentException(SoundstageIR.Core.TextCatalog.T("TC991C1E436"));
        double seconds=0;
        if(json.RootElement.TryGetProperty("format",out var format)&&format.TryGetProperty("duration",out var duration))double.TryParse(duration.GetString(),NumberStyles.Float,Invariant,out seconds);
        string folder=OutputNames.NewDirectory(Path.GetFullPath(options.OutputParent),result.Project);
        Directory.CreateDirectory(folder);
        string decoded=Path.Combine(folder,"work-input.f32"),rendered=Path.Combine(folder,"work-rendered.f32"),ir=Path.Combine(folder,"work-matrix.wav"),mastered=Path.Combine(folder,"work-mastered.f32");
        string suffix=options.Format switch{AudioFormat.Flac=>".flac",AudioFormat.Aac=>".m4a",_=>".wav"};
        string stem=OutputNames.Label(result.Project)+"_"+Path.GetFileNameWithoutExtension(input);if(stem.Length>90)stem=stem[..90];
        string output=Path.Combine(folder,stem+SoundstageIR.Core.TextCatalog.T("TAA083C6923")+suffix),partial=Path.Combine(folder,"encoding"+suffix);
        int sr=result.Project.SampleRate;string rate=sr.ToString(Invariant);
        try
        {
            progress?.Report((0,SoundstageIR.Core.TextCatalog.T("TB6097839D1")));
            string decodeFilter=$"aresample={sr}:resampler=soxr:precision=28"+(channels==1?",pan=stereo|c0=c0|c1=c0":"");
            await RunAsync(ffmpeg,["-v","error","-nostdin","-y","-i",input,"-map","0:a:0","-vn","-af",decodeFilter,"-c:a","pcm_f32le","-f","f32le",decoded,"-progress","pipe:1","-nostats"],
                t=>progress?.Report((Math.Clamp(t/Math.Max(1,seconds),0,1)*.15,SoundstageIR.Core.TextCatalog.T("T9463690657"))),cancellation);
            long frames=new FileInfo(decoded).Length/8;
            if(frames==0)throw new ArgumentException(SoundstageIR.Core.TextCatalog.T("TFE7C3B9354"));
            int taps=result.Kernels.Max(k=>k.Length);long total=checked(frames+taps-1);
            Exporter.WriteWave(ir,result.Kernels,sr);
            // File channel order: L->left, R->left, L->right, R->right.
            // Disable BOTH old/new FFmpeg IR normalization controls. No extra dry mix.
            string graph=$"[0:a]pan=4c|c0=c0|c1=c1|c2=c0|c3=c1,apad=pad_len={taps-1}[paths];"+
                "[paths][1:a]afir=dry=1:wet=1:irnorm=-1:gtype=none:irlink=0:irgain=1:precision=double:maxir=60:minp=4096:maxp=16384[convolved];"+
                $"[convolved]pan=stereo|c0=c0+c1|c1=c2+c3,atrim=end_sample={total}[out]";
            await RunAsync(ffmpeg,["-v","error","-nostdin","-y","-f","f32le","-ar",rate,"-ac","2","-i",decoded,"-i",ir,"-filter_complex",graph,"-map","[out]","-c:a","pcm_f32le","-f","f32le",rendered,"-progress","pipe:1","-nostats"],
                t=>progress?.Report((.15+Math.Clamp(t/(total/(double)sr),0,1)*.35,SoundstageIR.Core.TextCatalog.T("T22D87B4EB5"))),cancellation);
            if(new FileInfo(rendered).Length!=total*8)throw new InvalidDataException(SoundstageIR.Core.TextCatalog.T("T647F5D3660"));
            progress?.Report((.51,SoundstageIR.Core.TextCatalog.T("T513D41CBA3")));
            double peak=Peak(rendered,cancellation);
            if(!double.IsFinite(peak))throw new InvalidDataException(SoundstageIR.Core.TextCatalog.T("T1E74B286B4"));
            var before=peak==0?new LoudnessMeasurement(null,null,0):await MeasureAsync(ffmpeg,rendered,sr,
                t=>progress?.Report((.51+Math.Clamp(t/(total/(double)sr),0,1)*.10,SoundstageIR.Core.TextCatalog.T("T6BAC9A40AA"))),cancellation);
            double gainDb=normalize&&before.IntegratedLufs is double measured?options.TargetLufs-measured:0;
            string audio=rendered;
            double safetyGainDb=0,limiterCeilingDb=0;
            LoudnessMeasurement? after=null;bool verified=false;
            for(int attempt=0;attempt<6;attempt++)
            {
                if(limit&&(attempt==0||!normalize))
                {
                    progress?.Report((.62,normalize?SoundstageIR.Core.TextCatalog.T("TBB266E8BA8"):SoundstageIR.Core.TextCatalog.T("TB3AB2B00D1")));
                    await ApplyMasteringAsync(ffmpeg,rendered,mastered,sr,total,gainDb,
                        t=>progress?.Report((.62+Math.Clamp(t/(total/(double)sr),0,1)*.14,SoundstageIR.Core.TextCatalog.T("TA565C40D65"))),cancellation,limiterCeilingDb);
                    audio=mastered;
                    if(new FileInfo(audio).Length!=total*8)throw new InvalidDataException(SoundstageIR.Core.TextCatalog.T("TC9E4E9B211"));
                }
                double masterPeak=Peak(audio,cancellation);
                if(!double.IsFinite(masterPeak))throw new InvalidDataException(SoundstageIR.Core.TextCatalog.T("T57B21E5D1F"));
                if(!limit&&options.Format!=AudioFormat.Wav&&masterPeak>1)
                    throw new InvalidOperationException(SoundstageIR.Core.TextCatalog.T("T5F9DEC16D0"));
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
                    t=>progress?.Report((.77+Math.Clamp(t/(total/(double)sr),0,1)*.08,SoundstageIR.Core.TextCatalog.T("T4636A3ED00"))),cancellation);
                progress?.Report((.86,SoundstageIR.Core.TextCatalog.T("T0B19CE03E8")));
                after=await MeasureAsync(ffmpeg,partial,null,
                    t=>progress?.Report((.86+Math.Clamp(t/(total/(double)sr),0,1)*.12,SoundstageIR.Core.TextCatalog.T("T048EF2ABB2"))),cancellation);
                if(!limit || after.TruePeakDbTp is not double tp || tp<=-.02){verified=true;break;}
                if(normalize)safetyGainDb-=Math.Max(0,tp)+.10;
                else limiterCeilingDb-=Math.Max(0,tp)+.10;
                progress?.Report((.62,normalize?SoundstageIR.Core.TextCatalog.T("TB717030138"):SoundstageIR.Core.TextCatalog.T("T9A49EE06D7")));
            }
            if(!verified)throw new InvalidDataException(SoundstageIR.Core.TextCatalog.T("T69AF22FCCB"));
            cancellation.ThrowIfCancellationRequested();
            string note=normalize&&before.IntegratedLufs==null?SoundstageIR.Core.TextCatalog.T("T535B30DF8A"):"";
            var report=new AudioRenderResult(output,folder,frames,total,sr,gainDb,peak)
                {Before=before,After=after,SafetyGainDb=safetyGainDb,TargetLufs=normalize?options.TargetLufs:null,Mode=options.Mode,LimiterCeilingDb=limit?limiterCeilingDb:null,Note=note};
            File.WriteAllText(Path.Combine(folder,"render.json"),ProjectIO.Serialize(new{inputName=Path.GetFileName(input),options.Format,mode=options.Mode.ToString(),normalize,report.LimiterCeilingDb,report.SampleRate,report.InputSamples,report.OutputSamples,report.TargetLufs,report.GainDb,report.SafetyGainDb,report.PeakBefore,report.Before,report.After,report.Note,kernelSamples=taps,zeroSample=result.ZeroSample,
                loudness="ITU-R BS.1770 / EBU R128 gated integrated loudness; custom -18 LUFS music default, not EBU broadcast target",
                limiter=limit?"4x oversampling, stereo linked, 0 dBFS ceiling, 5 ms lookahead / 50 ms release, latency compensated; decoded output true-peak verification; normalization uses common scalar correction, limit-only lowers limiter threshold without whole-song attenuation":"bypass",
                kernelRoutes=result.OutputRoutes}));
            if(result.ExportProject==null)ProjectIO.Save(result.Project,Path.Combine(folder,"project.json"));else File.WriteAllText(Path.Combine(folder,"project.json"),ProjectIO.Serialize(result.ExportProject));
            static string Db(double? value,string unit)=>value is double v?$"{v:F2} {unit}":SoundstageIR.Core.TextCatalog.T("T3ED0E6BB5C");
            File.WriteAllText(Path.Combine(folder,SoundstageIR.Core.TextCatalog.T("T0EF9B00F59")),SoundstageIR.Core.TextCatalog.F("T591813D222", sr, (normalize?SoundstageIR.Core.TextCatalog.T("T42287A0F96"):limit?SoundstageIR.Core.TextCatalog.T("T025876C5D8"):SoundstageIR.Core.TextCatalog.T("T9ECCCB1ECF")), Db(before.IntegratedLufs,"LUFS"), Db(after?.IntegratedLufs,"LUFS"), Db(after?.TruePeakDbTp,"dBTP"), gainDb, safetyGainDb, (limit?SoundstageIR.Core.TextCatalog.F("TC38BA8B915", limiterCeilingDb):""), note));
            File.Move(partial,output);progress?.Report((1,SoundstageIR.Core.TextCatalog.T("T24ACFE2F65")+output));return report;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(folder,SoundstageIR.Core.TextCatalog.T("TEC67973FB9")),ex is OperationCanceledException?SoundstageIR.Core.TextCatalog.T("TE2532F7DD8"):ex.Message);
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
        using var process=Process.Start(start)??throw new InvalidOperationException(SoundstageIR.Core.TextCatalog.T("T52BD81AEF9"));
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
            if(process.ExitCode!=0)throw new InvalidOperationException(SoundstageIR.Core.TextCatalog.T("T8C0282BFA0")+(error.Length>3000?error[^3000..]:error));
            return diagnostics?error:string.Join("\n",lines);
        }
        catch(OperationCanceledException)
        {
            try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
            await process.WaitForExitAsync(CancellationToken.None);await errors;throw;
        }
    }
}
