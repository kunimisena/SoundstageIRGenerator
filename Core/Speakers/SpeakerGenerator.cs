namespace SoundstageIR.Core.Speakers;
public static class SpeakerGenerator
{
    static string L(string zh,string en)=>TextCatalog.English?en:zh;
    public static SpeakerResult Generate(SpeakerProject project,IProgress<(double Fraction,string Message)>? progress=null,CancellationToken ct=default)
    {
        var p=ProjectIO.Clone(project);p.Validate();ct.ThrowIfCancellationRequested();
        double[][] target,drive,predicted,playback=[],inverse=[];double wet,zero;int delay=0;double projection=-300,error=-300;
        var warnings=new List<string>();int sr=p.Field.SampleRate;GenerationResult? analysis=null,playbackAnalysis=null;InverseResponse? inverseResponse=null;
        if(p.Mode==SpeakerMode.SimpleReverb)
        {
            progress?.Report((.05,L("生成左右混响核","Generating left and right kernels")));
            var q=ProjectIO.Clone(p.Field);q.HeadModel=HeadModelKind.Sphere;q.CenterEqStrengthPercent=0;q.StrictMirror=false;
            var left=NoiseKernel.Generate(p.Left,sr,q.Seed,p.NoiseId,0,ct);
            var right=NoiseKernel.Generate(p.LinkParameters?p.Left:p.Right,sr,q.Seed,p.NoiseId,1,ct);
            double[][] dry=q.Direct.Enabled?[[1],[0],[0],[1]]:[[0],[0],[0],[0]],reverb=[left,[0],[0],right];
            EnergyBalance.Mix(dry,reverb,q.ReflectionEnergyPercent,sr);
            progress?.Report((.25,L("校正干湿和音色","Calibrating mix and tone")));
            var balance=WetBalanceSolver.Solve(q,dry,reverb,ct);
            foreach(var h in reverb)Generator.Scale(h,Math.Pow(10,balance.GainDb/20));
            var raw=Enumerable.Range(0,4).Select(c=>Dsp.Sum(dry[c],reverb[c])).ToArray();
            var synthesis=SpectralSynthesis.Apply(q,raw,dry,ct);
            drive=synthesis.Kernels;
            double reference=new[]{0,3}.SelectMany(c=>Dsp.SmoothedDbAt(drive[c],sr,q.Smooth1)).Average();
            double scale=Math.Pow(10,(q.OutputDb-reference)/20);foreach(var h in drive)Generator.Scale(h,scale);
            var finalDry=synthesis.Direct;foreach(var h in finalDry)Generator.Scale(h,scale);
            wet=EnergyBalance.Percent(EnergyBalance.Energy(finalDry,sr),EnergyBalance.Energy(EnergyBalance.Subtract(drive,finalDry),sr));
            target=drive;predicted=drive;zero=0;
            analysis=new(){Project=p.Field,Raw=raw,Kernels=drive,DirectPaths=dry,FinalDirectPaths=finalDry,
                FinalReflectionPaths=EnergyBalance.Subtract(drive,finalDry),EarEq=synthesis.EarEq,SecondEq=synthesis.SecondEq,
                Bandpass=synthesis.Bandpass,HeadEq=synthesis.HeadEq,EqReports=synthesis.Reports,Contributions=[],
                ReflectionPercentAfterEq=wet,WetBalance=balance};
        }
        else
        {
            var generated=Generator.Generate(p.Field,new ProgressForward(progress,.0,.65),ct);analysis=generated;target=generated.Kernels;wet=generated.ReflectionPercentAfterEq;zero=generated.ZeroSample;
            warnings.AddRange(generated.Warnings);
            progress?.Report((.67,L("建立实际音箱模型","Modelling actual speakers")));playbackAnalysis=SpeakerPlayback.Calibrated(p,ct);playback=playbackAnalysis.Kernels;
            progress?.Report((.75,L("设计播放端求逆","Designing playback inverse")));var solution=SpeakerTransform.Design(playback,target,sr,p.MaximumInverseGainDb,ct);
            inverse=solution.Kernels;inverseResponse=solution.Response;delay=solution.Delay;projection=solution.ProjectionErrorDb;
            progress?.Report((.84,L("合成音箱驱动核","Rendering speaker drives")));drive=solution.Kernels;
            // Verify the actual float32 export, not an ideal frequency-domain inverse.
            drive=drive.Select(h=>h.Select(v=>(double)(float)v).ToArray()).ToArray();
            progress?.Report((.92,L("验证预测耳端响应","Verifying predicted ear response")));predicted=SpeakerInverse.Multiply(playback,drive,ct);
            error=RelativeError(predicted,target,delay,sr);
            if(projection>-60)warnings.Add(L($"求逆截断残差：{projection:0.0} dB",$"Inverse projection residual: {projection:0.0} dB"));
            if(error>-12)warnings.Add(L($"播放模型还原残差：{error:0.0} dB",$"Playback reconstruction residual: {error:0.0} dB"));
        }
        int length=drive.Max(h=>h.Length);for(int c=0;c<4;c++){Array.Resize(ref drive[c],length);for(int i=0;i<length;i++)drive[c][i]=(float)drive[c][i];}
        if(drive.Any(h=>h.Any(v=>!double.IsFinite(v))))throw new InvalidOperationException("Non-finite output / 输出存在非有限数值");
        double peak=new[]{0,1}.Max(row=>Dsp.PowerAt(Dsp.Sum(drive[row*2],drive[row*2+1]),sr).Max());
        if(Dsp.Db(peak)>18)warnings.Add(L($"同相最大增益：{Dsp.Db(peak):0.0} dB",$"Peak coherent gain: {Dsp.Db(peak):0.0} dB"));
        var result=new SpeakerResult(p,drive,target,predicted,playback,inverse,delay,projection,error,Dsp.Db(peak),wet,zero+delay,warnings){TargetAnalysis=analysis,PlaybackAnalysis=playbackAnalysis,InverseResponse=inverseResponse};
        if(p.Mode==SpeakerMode.SpatialField){progress?.Report((.97,L("检查相消与位置敏感性","Checking cancellation and position sensitivity")));result=result with {Checks=SpeakerRiskCheck.Final(result,ct)};}
        progress?.Report((1,L("已完成","Ready")));return result;
    }
    public static double RelativeError(double[][] actual,double[][] target,int delay,int sr)
    {
        int n=Dsp.Pow2(Math.Max(actual.Max(h=>h.Length),target.Max(h=>h.Length)+delay));double total=0,error=0;
        for(int c=0;c<4;c++)
        {var a=Dsp.Spectrum(actual[c],n);var t=Dsp.Spectrum(target[c],n);for(int k=(int)Math.Ceiling(20.0*n/sr);k<=Math.Min(n/2,(int)(20000.0*n/sr));k++)
            {var ideal=t[k]*System.Numerics.Complex.FromPolarCoordinates(1,-2*Math.PI*k*delay/n);total+=Dsp.Power(ideal);error+=Dsp.Power(a[k]-ideal);}}
        return Dsp.Db(error/Math.Max(1e-30,total));
    }
    sealed class ProgressForward(IProgress<(double Fraction,string Message)>? target,double start,double scale):IProgress<(double Fraction,string Message)>
    {public void Report((double Fraction,string Message) p)=>target?.Report((start+scale*p.Fraction,p.Message));}
}
public static class SpeakerExporter
{
    public static readonly string[] Routes=["L_to_LeftSpeaker","R_to_LeftSpeaker","L_to_RightSpeaker","R_to_RightSpeaker"];
    public static string Export(SpeakerResult result,string parent)
    {
        var p=result.Project;string folder=OutputNames.NewDirectory(parent,p.Field);string FileName(string suffix)=>OutputNames.File(p.Field,suffix);
        bool simple=p.Mode==SpeakerMode.SimpleReverb;var routes=simple?new[]{0,3}:new[]{0,1,2,3};
        foreach(int c in routes)Exporter.WriteWave(Path.Combine(folder,FileName(Routes[c]+".wav")),[result.Drive[c]],result.SampleRate);
        if(!simple)Exporter.WriteWave(Path.Combine(folder,FileName("SpeakerMatrix_LL_RL_LR_RR.wav")),result.Drive,result.SampleRate);
        p.Save(Path.Combine(folder,FileName("speaker-project.json")));
        string config=Path.Combine(folder,FileName(TextCatalog.English?"Equalizer_APO_Config.txt":"Equalizer_APO配置.txt"));
        var lines=new List<string>{$"# {p.Field.Name} | {p.Mode} | {result.SampleRate} Hz", "# Speaker output / 音箱输出"};
        if(simple)
        {lines.Add("Channel: L");lines.Add("Convolution: "+Path.Combine(folder,FileName(Routes[0]+".wav")));lines.Add("Channel: R");lines.Add("Convolution: "+Path.Combine(folder,FileName(Routes[3]+".wav")));}
        else
        {lines.Add("Copy: SLL=L SLR=L SRL=R SRR=R");foreach(var (name,c) in new[]{("SLL",0),("SRL",1),("SLR",2),("SRR",3)}){lines.Add("Channel: "+name);lines.Add("Convolution: "+Path.Combine(folder,FileName(Routes[c]+".wav")));}lines.Add("Copy: L=SLL+SRL R=SLR+SRR");}
        lines.Add("Channel: L R");File.WriteAllLines(config,lines);
        File.WriteAllText(Path.Combine(folder,FileName("Import_Guide.txt")),TextCatalog.English
            ?$"Import this file in Equalizer APO:\n{config}\n\nKeep this directory in place: the configuration uses absolute WAV paths. Set the playback device to {result.SampleRate} Hz. Complex mode uses the saved speaker positions and a fixed, forward-facing FABIAN listener.\n"
            :$"在 Equalizer APO 中导入：\n{config}\n\n配置使用 WAV 绝对路径，请保持此目录位置不变。设备采样率设为 {result.SampleRate} Hz。复杂模式按已保存的音箱摆位和固定朝前的 FABIAN 听音位置计算。\n");
        File.WriteAllText(Path.Combine(folder,FileName("metadata.json")),ProjectIO.Serialize(new{p.Mode,result.SampleRate,result.Duration,result.LatencySamples,result.ZeroSample,result.RelativeErrorDb,result.InverseProjectionDb,result.MaximumDriveGainDb,result.WetPercent,result.Warnings,result.Checks,positionSamples="centre, lateral +/-5 cm, fore-aft +/-5 cm, yaw +/-5 degrees; same fixed C and calibration",p.LeftSpeaker,p.RightSpeaker,p.AirAbsorption,p.MaximumInverseGainDb,inverseNotchControl="1/6-octave local weakest-singular-value power floor; complex matrix phase retained",routes=routes.Select(c=>Routes[c]),reference="F = calibrated headphone free field at actual positions; T = calibrated target headphone field; solve F*C=T with shared causal delay; both F and T include head-power EQ, smooth EQ and output bandpass; wet fraction is target-ear fraction",playbackGeometry="far-field FABIAN; no near-field correction; optional per-speaker air absorption; relative distance level and travel time retained",normalization="target common calibration retained; no drive re-EQ or individual path normalization"}));
        if(!simple)
        {
            string reference=Path.Combine(folder,"FreeField_Reference");Directory.CreateDirectory(reference);
            var fs=new List<string>{"# Calibrated free-field playback F", "Copy: FLL=L FLR=L FRL=R FRR=R"};
            foreach(var (name,c) in new[]{("FLL",0),("FRL",1),("FLR",2),("FRR",3)})
            {
                string wave=Path.Combine(reference,Generator.RouteNames[c]+".wav");Exporter.WriteWave(wave,[result.Playback[c]],result.SampleRate);
                fs.Add("Channel: "+name);fs.Add("Convolution: "+wave);
            }
            fs.Add("Copy: L=FLL+FRL R=FLR+FRR");fs.Add("Channel: L R");
            string freeConfig=Path.Combine(reference,"FreeField.txt");File.WriteAllLines(freeConfig,fs);
            string cascade=Path.Combine(folder,FileName("Cascade_Test.txt"));
            File.WriteAllLines(cascade,["# Headphone comparison: transform C first, calibrated free field F second.","# Import this file by itself; do not enable the main C configuration again.","Include: "+config,"Include: "+freeConfig]);
            File.AppendAllText(Path.Combine(folder,FileName("Import_Guide.txt")),TextCatalog.English
                ?$"\nHeadphone cascade comparison: import {cascade} by itself. It runs C then F and reproduces the target headphone field, subject to regularization and a common {result.LatencySamples*1000.0/result.SampleRate:0.00} ms delay. The FreeField_Reference subfolder contains the exact calibrated F used in the solve.\n"
                :$"\n耳机串联对照：单独导入 {cascade}，先运行 C，再运行 F；不要同时启用上面的主配置。结果对应原耳机版目标声场，包含求逆约束残差和 {result.LatencySamples*1000.0/result.SampleRate:0.00} ms 共同延迟。FreeField_Reference 子目录是此次求逆使用的完整自由场 F。\n");
        }
        if(result.Checks?.HasRisk==true)File.AppendAllText(Path.Combine(folder,FileName("Import_Guide.txt")),"\n"+result.Checks.Text+"\n");
        var notice=Path.Combine(AppContext.BaseDirectory,"FABIAN-NOTICE.txt");if(!simple&&System.IO.File.Exists(notice))System.IO.File.Copy(notice,Path.Combine(folder,"FABIAN-NOTICE.txt"));return folder;
    }
}
