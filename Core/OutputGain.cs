namespace SoundstageIR.Core;
public readonly record struct OutputGainReport(double CommonGainDb,double FinalPeakDb,double SamplePeakBoundDb);
public static class OutputGain
{
    // Measure per-ear input-path amplitude sums for diagnostics only.
    // Calibration is supplied by the generator; peaks never change its gain.
    public static double FrequencyPeakDb(double[][] paths)
    {
        int n=Dsp.Pow2(checked(Math.Max(65536,paths.Max(h=>h.Length)*2)));double peak=0;
        for(int ear=0;ear<2;ear++)
        {
            var a=Dsp.Spectrum(paths[ear*2],n);var b=Dsp.Spectrum(paths[ear*2+1],n);
            for(int k=0;k<=n/2;k++)peak=Math.Max(peak,a[k].Magnitude+b[k].Magnitude);
        }
        if(!double.IsFinite(peak)||peak<=0)throw new InvalidOperationException("成品响应为空或包含无效数值。");
        return 20*Math.Log10(peak);
    }
    public static OutputGainReport Apply(double[][] paths,double requestedGainDb)
    {
        if(paths.Any(h=>h.Any(v=>!double.IsFinite(v)))||!double.IsFinite(requestedGainDb))throw new InvalidOperationException("输出增益或卷积核包含无效数值。");
        double gain=Math.Pow(10,requestedGainDb/20);
        if(!double.IsFinite(gain)||gain==0)throw new InvalidOperationException("输出增益超出双精度表示范围。");
        foreach(var h in paths)for(int i=0;i<h.Length;i++)
        {
            float sample=(float)(h[i]*gain);
            if(!float.IsFinite(sample))throw new InvalidOperationException("卷积核无法保存为有效 float32 数据。");
            h[i]=sample;
        }
        double finalPeak=FrequencyPeakDb(paths);
        double bound=Math.Max(paths[0].Sum(Math.Abs)+paths[1].Sum(Math.Abs),paths[2].Sum(Math.Abs)+paths[3].Sum(Math.Abs));
        return new(requestedGainDb,finalPeak,20*Math.Log10(bound));
    }
}
