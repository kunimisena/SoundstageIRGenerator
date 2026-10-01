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
        File.WriteAllText(Path.Combine(path,OutputNames.File(r.Project,"metadata.json")),ProjectIO.Serialize(new{version=5,headModel=r.Project.HeadModel.ToString(),r.Project.FabianCtfCompensation,headBandwidthExtension=new{active=r.Project.HeadModel==HeadModelKind.Fabian,lowHz=20,highHz=20000,magnitude="native in-band; constant boundary magnitude outside",magnitudeFloorDb=(double?)null},headReferenceMethod="equal-power unit impulses, distinct enabled directions, mean binaural power, no cross terms or smoothing",headReferenceActive=r.HeadEq.Length>1,headDescription=TextCatalog.Diagnostic(HeadRenderer.Description(r.Project)),r.Project.EarEqStrengthPercent,r.Project.CenterEqStrengthPercent,r.Project.ReflectionEnergyPercent,r.ReflectionPercentBeforeEq,r.ReflectionPercentAfterEq,r.WetBalance,energyReference="20 Hz-20 kHz flat PSD, independent equal-power inputs, both ears, separate components",r.Project.SampleRate,r.Project.Direct.AirAbsorptionDistance,r.Duration,r.ZeroSample,zeroMs=r.ZeroSample*1000/r.Project.SampleRate,r.CommonGainDb,r.PeakBoundDb,r.MaxBinGainDb,r.EqResidualDb,r.SynthesisFftLength,r.CorrectionSupport,r.ProjectionErrorDb,r.DiscardedEnergyDb,r.Seconds,routes=Generator.RouteNames,eq=r.Project.Equalize?(r.Project.StrictMirror?"shared single EQ":"per-ear EQ then shared complex-ear-sum EQ"):"bypass",bandpass=new{passLowHz=20,passHighHz=20000,stopLowHz=10,stopHighHz=Math.Min(22000,r.Project.SampleRate*.5*.995),targetStopDb=-100,diagnosticFftSamples=r.Bandpass.Length,phase="minimum-phase target combined with EQ in the frequency domain"},eqBoostLimitDb=(double?)null,eqCutLimitDb=(double?)null,r.OutputReferenceDb,r.EqResidualReference,r.EqReports,r.Warnings,outputGain="common reference calibration plus requested output gain; peak diagnostics are informational",air="ISO 9613-1, 20 C, 50% RH, standard pressure"}));
        File.WriteAllText(Path.Combine(path,OutputNames.File(r.Project,SoundstageIR.Core.TextCatalog.T("T891AFCFA2D"))),SoundstageIR.Core.TextCatalog.F("T4DA1AD7D43", r.Project.Name, r.Project.SampleRate, r.Duration, (r.Project.Equalize?SoundstageIR.Core.TextCatalog.T("T3FAD179C2A"):SoundstageIR.Core.TextCatalog.T("T773C7AA03E")), r.ZeroSample, r.ZeroSample*1000/r.Project.SampleRate, (r.Project.Direct.Enabled?r.Project.ReflectionEnergyPercent:100), r.ReflectionPercentBeforeEq, r.ReflectionPercentAfterEq, r.WetBalance.GainDb, r.WetBalance.Evaluations, r.CommonGainDb, r.MaxBinGainDb, r.PeakBoundDb, r.CorrectionSupport, r.ProjectionErrorDb, r.DiscardedEnergyDb, TextCatalog.Diagnostic(HeadRenderer.Description(r.Project))));
        File.AppendAllText(Path.Combine(path,OutputNames.File(r.Project,SoundstageIR.Core.TextCatalog.T("T891AFCFA2D"))),SoundstageIR.Core.TextCatalog.F("T81C290CCD5", r.OutputReferenceDb, TextCatalog.Diagnostic(r.EqResidualReference))+string.Join("\n",r.Warnings.Select(TextCatalog.Diagnostic))+"\n");
        ApoExporter.WriteConfig(r,path);
        File.AppendAllText(Path.Combine(path,OutputNames.File(r.Project,SoundstageIR.Core.TextCatalog.T("T891AFCFA2D"))),SoundstageIR.Core.TextCatalog.F("TFE48EFE512", r.Project.Equalize, r.Project.EarEqStrengthPercent, r.Project.CenterEqStrengthPercent));
        File.AppendAllText(Path.Combine(path,OutputNames.File(r.Project,SoundstageIR.Core.TextCatalog.T("T891AFCFA2D"))),SoundstageIR.Core.TextCatalog.F("T10E72641EB", (r.HeadEq.Length>1?SoundstageIR.Core.TextCatalog.T("TF4F0EAD111"):SoundstageIR.Core.TextCatalog.T("T3FD47EDCE4"))));
        if(r.Project.HeadModel==HeadModelKind.Fabian)
        {
            var notice=Path.Combine(AppContext.BaseDirectory,"FABIAN-NOTICE.txt");
            if(File.Exists(notice))File.Copy(notice,Path.Combine(path,"FABIAN-NOTICE.txt"),true);
        }
        return path;
    }
    public static void WriteWave(string path,double[][] channels,int sr)
    {
        int len=channels[0].Length;if(channels.Any(h=>h.Length!=len))throw new ArgumentException(SoundstageIR.Core.TextCatalog.T("T8480B95BD5"));
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
            if(tag=="data"){if(format!=3||bits!=32||channels==0)throw new InvalidDataException(SoundstageIR.Core.TextCatalog.T("T1F623819E1"));int count=n/channels/4;var a=Enumerable.Range(0,channels).Select(_=>new double[count]).ToArray();for(int i=0;i<count;i++)for(int c=0;c<channels;c++)a[c][i]=r.ReadSingle();return(sr,a);}r.BaseStream.Position=end+(n%2);}
        throw new InvalidDataException(SoundstageIR.Core.TextCatalog.T("T446EE246BB"));
    }
}
