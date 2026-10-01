using System.Text;
namespace SoundstageIR.Core;
public static class Exporter
{
    public static string Export(GenerationResult r,string parent)
    {
        string path=OutputNames.NewDirectory(parent,r.Project);
        for(int i=0;i<4;i++)WriteWave(Path.Combine(path,OutputNames.File(r.Project,Generator.RouteNames[i]+".wav")),[r.Kernels[i]],r.Project.SampleRate);
        WriteWave(Path.Combine(path,OutputNames.File(r.Project,"Matrix_PATHS_LL_RL_LR_RR.wav")),r.Kernels,r.Project.SampleRate);
        ProjectIO.Save(r.Project,Path.Combine(path,OutputNames.File(r.Project,"project.json")));
        File.WriteAllText(Path.Combine(path,OutputNames.File(r.Project,"metadata.json")),ProjectIO.Serialize(new{version=5,headModel=r.Project.HeadModel.ToString(),r.Project.FabianCtfCompensation,headBandwidthExtension=new{active=r.Project.HeadModel==HeadModelKind.Fabian,lowHz=20,highHz=20000,magnitude="native in-band; constant boundary magnitude outside",magnitudeFloorDb=(double?)null},headReferenceMethod="equal-power unit impulses, distinct enabled directions, mean binaural power, no cross terms or smoothing",headReferenceActive=r.HeadEq.Length>1,headDescription=HeadRenderer.Description(r.Project),r.Project.EarEqStrengthPercent,r.Project.CenterEqStrengthPercent,r.Project.ReflectionEnergyPercent,r.ReflectionPercentBeforeEq,r.ReflectionPercentAfterEq,r.WetBalance,energyReference="20 Hz-20 kHz flat PSD, independent equal-power inputs, both ears, separate components",r.Project.SampleRate,r.Project.Direct.AirAbsorptionDistance,r.Duration,r.ZeroSample,zeroMs=r.ZeroSample*1000/r.Project.SampleRate,r.CommonGainDb,r.PeakBoundDb,r.MaxBinGainDb,r.EqResidualDb,r.SynthesisFftLength,r.CorrectionSupport,r.ProjectionErrorDb,r.DiscardedEnergyDb,r.Seconds,routes=Generator.RouteNames,eq=r.Project.Equalize?(r.Project.StrictMirror?"shared single EQ":"per-ear EQ then shared complex-ear-sum EQ"):"bypass",bandpass=new{passLowHz=20,passHighHz=20000,stopLowHz=10,stopHighHz=Math.Min(22000,r.Project.SampleRate*.5*.995),targetStopDb=-100,diagnosticFftSamples=r.Bandpass.Length,phase="minimum-phase target combined with EQ in the frequency domain"},eqBoostLimitDb=(double?)null,eqCutLimitDb=(double?)null,r.OutputReferenceDb,r.EqResidualReference,r.EqReports,r.Warnings,outputGain="common reference calibration plus requested output gain; peak diagnostics are informational",air="ISO 9613-1, 20 C, 50% RH, standard pressure"}));
        File.WriteAllText(Path.Combine(path,OutputNames.File(r.Project,"使用说明.txt")),$"Soundstage IR Generator · 耳机空间音效卷积核 / {r.Project.Name}\n{r.Project.SampleRate} Hz · IEEE float32 · {r.Duration:F3} 秒\n四路径顺序：L 输入→左耳、R 输入→左耳、L 输入→右耳、R 输入→右耳。\nMatrix 文件按上述顺序打包，供矩阵卷积器读取。\n{(r.Project.Equalize?"EQ 已写入四条核":"EQ 已旁路，最终带通仍生效")}；四条路径共同使用，保留相对电平和延时。\n显示的直达声零点对应 WAV 第 {r.ZeroSample:F3} 个样本；共同前导 {r.ZeroSample*1000/r.Project.SampleRate:F3} ms。\n混响分量能量占比：目标 {(r.Project.Direct.Enabled?r.Project.ReflectionEnergyPercent:100):F3}%，EQ 前 {r.ReflectionPercentBeforeEq:F3}%，最终 {r.ReflectionPercentAfterEq:F3}%。混响整体预修正 {r.WetBalance.GainDb:+0.000;-0.000;0} dB（{r.WetBalance.Evaluations} 次频谱计算）。基准为 20 Hz–20 kHz 等功率独立输入、双耳合计。\n共同输出标定 {r.CommonGainDb:+0.00;-0.00;0} dB。\n最大频点路径幅度和 {r.MaxBinGainDb:F2} dB；任意输入峰值的保守上界 {r.PeakBoundDb:F2} dB。可据此设置播放增益。\n最终四路径以 20 Hz–20 kHz 平直带通为目标，与平滑最小相位校正一起在频域合成。20 Hz 与 20 kHz 位于目标通带。\n校正时间预算 {r.CorrectionSupport} 样本；有限长度合成偏差 {r.ProjectionErrorDb:F3} dB，尾部移除能量 {r.DiscardedEnergyDb:F1} dB。\n人头模型：{HeadRenderer.Description(r.Project)}。\n");
        File.AppendAllText(Path.Combine(path,OutputNames.File(r.Project,"使用说明.txt")),$"实际参考电平 {r.OutputReferenceDb:F2} dB；平滑残差参考：{r.EqResidualReference}。\n"+string.Join("\n",r.Warnings)+"\n");
        ApoExporter.WriteConfig(r,path);
        File.AppendAllText(Path.Combine(path,OutputNames.File(r.Project,"使用说明.txt")),$"空间 EQ：总开关 {r.Project.Equalize}，一级 {r.Project.EarEqStrengthPercent:0.##}%，二级 {r.Project.CenterEqStrengthPercent:0.##}%（严格镜像不使用二级）。\n");
        File.AppendAllText(Path.Combine(path,OutputNames.File(r.Project,"使用说明.txt")),$"人头参考精确校正：{(r.HeadEq.Length>1?"启用":"关闭")}；各启用方向等能量单位冲激测试，双耳平均功率非相干叠加，直接求逆到平直目标；不使用混响随机核或平滑。\n");
        if(r.Project.HeadModel==HeadModelKind.Fabian)
        {
            var notice=Path.Combine(AppContext.BaseDirectory,"FABIAN-NOTICE.txt");
            if(File.Exists(notice))File.Copy(notice,Path.Combine(path,"FABIAN-NOTICE.txt"),true);
        }
        return path;
    }
    public static void WriteWave(string path,double[][] channels,int sr)
    {
        int len=channels[0].Length;if(channels.Any(h=>h.Length!=len))throw new ArgumentException("WAV 通道长度不一致。");
        int bytes=checked(len*channels.Length*4);
        using var w=new BinaryWriter(File.Create(path));w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+bytes);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)3);w.Write((short)channels.Length);w.Write(sr);w.Write(sr*channels.Length*4);w.Write((short)(channels.Length*4));w.Write((short)32);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(bytes);
        for(int i=0;i<len;i++)foreach(var ch in channels)w.Write((float)ch[i]);
    }
    public static (int SampleRate,double[][] Channels) ReadWave(string path)
    {
        using var r=new BinaryReader(File.OpenRead(path));if(new string(r.ReadChars(4))!="RIFF")throw new InvalidDataException();r.ReadInt32();if(new string(r.ReadChars(4))!="WAVE")throw new InvalidDataException();int sr=0,channels=0,format=0,bits=0;
        while(r.BaseStream.Position<r.BaseStream.Length){string tag=new(r.ReadChars(4));int n=r.ReadInt32();long end=r.BaseStream.Position+n;
            if(tag=="fmt ")
            {
                format=r.ReadUInt16();channels=r.ReadInt16();sr=r.ReadInt32();r.ReadInt32();r.ReadInt16();bits=r.ReadInt16();
                if(format==0xfffe&&n>=40)
                {
                    int extension=r.ReadUInt16();r.ReadUInt16();r.ReadUInt32();var subtype=new Guid(r.ReadBytes(16));
                    if(extension>=22&&subtype==new Guid("00000003-0000-0010-8000-00aa00389b71"))format=3;
                }
            }
            if(tag=="data"){if(format!=3||bits!=32||channels==0)throw new InvalidDataException("需要 float32 WAV。");int count=n/channels/4;var a=Enumerable.Range(0,channels).Select(_=>new double[count]).ToArray();for(int i=0;i<count;i++)for(int c=0;c<channels;c++)a[c][i]=r.ReadSingle();return(sr,a);}r.BaseStream.Position=end+(n%2);}
        throw new InvalidDataException("缺少 WAV 数据。");
    }
}
