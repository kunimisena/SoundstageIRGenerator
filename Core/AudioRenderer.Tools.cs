namespace SoundstageIR.Core;

public sealed record AudioTools(string Ffmpeg,string Ffprobe);
public static partial class AudioRenderer
{
    public static AudioTools ResolveTools(string? ffmpegPath=null)
    {
        string ffmpeg=string.IsNullOrWhiteSpace(ffmpegPath)?FindTool("ffmpeg"):Path.GetFullPath(ffmpegPath);
        if(!File.Exists(ffmpeg))throw new FileNotFoundException("找不到选定的 ffmpeg.exe，请重新选择。",ffmpeg);
        if(!Path.GetFileName(ffmpeg).Equals("ffmpeg.exe",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择 ffmpeg.exe 文件。");
        string probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");
        if(!File.Exists(probe))throw new FileNotFoundException("FFmpeg 同目录缺少 ffprobe.exe，请选择完整构建中的 ffmpeg.exe。",probe);
        return new(Path.GetFullPath(ffmpeg),probe);
    }

    public static async Task<string> CheckToolsAsync(AudioTools tools,CancellationToken cancellation=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            string version=await RunAsync(tools.Ffmpeg,["-version"],null,timeout.Token);
            string probe=await RunAsync(tools.Ffprobe,["-version"],null,timeout.Token);
            if(!version.StartsWith("ffmpeg version",StringComparison.OrdinalIgnoreCase)||!probe.StartsWith("ffprobe version",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("所选工具没有返回 FFmpeg / ffprobe 版本信息。");
            string afir=await RunAsync(tools.Ffmpeg,["-hide_banner","-h","filter=afir"],null,timeout.Token);
            if(!afir.Contains("irnorm")||!afir.Contains("precision"))
                throw new InvalidDataException("此 FFmpeg 缺少歌曲卷积所需的 afir 参数，请使用较新的完整构建。");
            // Exercise the actual resampler and mastering filters without audio devices or files.
            await RunAsync(tools.Ffmpeg,["-v","error","-nostdin","-f","lavfi","-i","anullsrc=r=48000:cl=stereo",
                "-t","0.05","-af","aresample=96000:resampler=soxr:precision=28,alimiter=latency=1,loudnorm=I=-18",
                "-f","null","-"],null,timeout.Token);
            return version.Split('\n')[0];
        }
        catch(OperationCanceledException) when(!cancellation.IsCancellationRequested)
        {throw new TimeoutException("FFmpeg 检查超时，请确认选择的是可运行的完整构建。");}
    }
}
