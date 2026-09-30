using System.Text;
namespace StatisticalField.Core;
public static class ApoExporter
{
    public static string Export(GenerationResult result,string executableDirectory)
    {
        string path=OutputNames.NewDirectory(Path.Combine(Path.GetFullPath(executableDirectory),"EqualizerAPO"),result.Project);
        WriteConfig(result,path);
        ProjectIO.Save(result.Project,Path.Combine(path,OutputNames.File(result.Project,"project.json")));
        return path;
    }
    public static void WriteConfig(GenerationResult result,string path)
    {
        var config=new StringBuilder("# Statistical Field Studio — 四路径双耳卷积\n# 配置使用绝对路径，更换位置后请重新导出。\n# 设备采样率须与 WAV 一致："+result.Project.SampleRate+" Hz\nCopy: LL=L LR=L RL=R RR=R\n");
        int[] routes=[0,2,1,3];string[] channels=["LL","LR","RL","RR"];
        for(int i=0;i<4;i++)
        {
            string file=Path.Combine(path,OutputNames.File(result.Project,Generator.RouteNames[routes[i]]+".wav"));
            Exporter.WriteWave(file,[result.Kernels[routes[i]]],result.Project.SampleRate);
            config.AppendLine("Channel: "+channels[i]).AppendLine("Convolution: "+file);
        }
        config.AppendLine("Copy: L=LL+RL R=LR+RR").AppendLine("Channel: L R");
        File.WriteAllText(Path.Combine(path,OutputNames.File(result.Project,"APO.txt")),config.ToString(),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path,OutputNames.File(result.Project,"APO使用说明.txt")),"在 Equalizer APO 中 Include 本目录中以 _APO.txt 结尾的配置文件。\n四条 WAV 使用绝对路径。更换文件夹或文件名后，请重新导出配置。\n卷积核已包含设定的空间 EQ、20 Hz–20 kHz 带通和共同增益，可直接使用。\n设备采样率："+result.Project.SampleRate+" Hz。请保留原有耳机 EQ。\n");
        File.AppendAllText(Path.Combine(path,OutputNames.File(result.Project,"APO使用说明.txt")),"人头模型："+HeadRenderer.Description(result.Project)+"。\n");
        File.AppendAllText(Path.Combine(path,OutputNames.File(result.Project,"APO使用说明.txt")),$"空间 EQ：总开关 {result.Project.Equalize}，一级 {result.Project.EarEqStrengthPercent:0.##}%，二级 {result.Project.CenterEqStrengthPercent:0.##}%（严格镜像不使用二级）。\n");
        File.AppendAllText(Path.Combine(path,OutputNames.File(result.Project,"APO使用说明.txt")),$"实际参考电平 {result.OutputReferenceDb:F2} dB。\n"+string.Join("\n",result.Warnings)+"\n");
        if(result.Project.HeadModel==HeadModelKind.Fabian)
        {
            var notice=Path.Combine(AppContext.BaseDirectory,"FABIAN-NOTICE.txt");
            if(File.Exists(notice))File.Copy(notice,Path.Combine(path,"FABIAN-NOTICE.txt"));
        }
    }
}
