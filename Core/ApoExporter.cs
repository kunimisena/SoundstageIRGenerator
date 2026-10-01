using System.Text;
namespace SoundstageIR.Core;
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
        var config=new StringBuilder(SoundstageIR.Core.TextCatalog.T("T9AB5A91FF3")+result.Project.SampleRate+" Hz\nCopy: LL=L LR=L RL=R RR=R\n");
        int[] routes=[0,2,1,3];string[] channels=["LL","LR","RL","RR"];
        for(int i=0;i<4;i++)
        {
            string file=Path.Combine(path,OutputNames.File(result.Project,Generator.RouteNames[routes[i]]+".wav"));
            Exporter.WriteWave(file,[result.Kernels[routes[i]]],result.Project.SampleRate);
            config.AppendLine("Channel: "+channels[i]).AppendLine("Convolution: "+file);
        }
        config.AppendLine("Copy: L=LL+RL R=LR+RR").AppendLine("Channel: L R");
        File.WriteAllText(Path.Combine(path,OutputNames.File(result.Project,"APO.txt")),config.ToString(),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path,OutputNames.File(result.Project,SoundstageIR.Core.TextCatalog.T("T9F362DB7C7"))),SoundstageIR.Core.TextCatalog.T("T3BF241DC47")+result.Project.SampleRate+SoundstageIR.Core.TextCatalog.T("T6370457031"));
        File.AppendAllText(Path.Combine(path,OutputNames.File(result.Project,SoundstageIR.Core.TextCatalog.T("T9F362DB7C7"))),SoundstageIR.Core.TextCatalog.T("T78B83067E3")+TextCatalog.Diagnostic(HeadRenderer.Description(result.Project))+"。\n");
        File.AppendAllText(Path.Combine(path,OutputNames.File(result.Project,SoundstageIR.Core.TextCatalog.T("T9F362DB7C7"))),SoundstageIR.Core.TextCatalog.F("TFE48EFE512", result.Project.Equalize, result.Project.EarEqStrengthPercent, result.Project.CenterEqStrengthPercent));
        File.AppendAllText(Path.Combine(path,OutputNames.File(result.Project,SoundstageIR.Core.TextCatalog.T("T9F362DB7C7"))),SoundstageIR.Core.TextCatalog.F("T777D29EC43", result.OutputReferenceDb)+string.Join("\n",result.Warnings.Select(TextCatalog.Diagnostic))+"\n");
        File.AppendAllText(Path.Combine(path,OutputNames.File(result.Project,SoundstageIR.Core.TextCatalog.T("T9F362DB7C7"))),SoundstageIR.Core.TextCatalog.F("T99551B705C", (result.Project.Direct.Enabled?result.Project.ReflectionEnergyPercent:100), result.ReflectionPercentAfterEq, result.WetBalance.GainDb));
        File.AppendAllText(Path.Combine(path,OutputNames.File(result.Project,SoundstageIR.Core.TextCatalog.T("T9F362DB7C7"))),SoundstageIR.Core.TextCatalog.F("T10E72641EB", (result.HeadEq.Length>1?SoundstageIR.Core.TextCatalog.T("TF4F0EAD111"):SoundstageIR.Core.TextCatalog.T("T3FD47EDCE4"))));
        if(result.Project.HeadModel==HeadModelKind.Fabian)
        {
            var notice=Path.Combine(AppContext.BaseDirectory,"FABIAN-NOTICE.txt");
            if(File.Exists(notice))File.Copy(notice,Path.Combine(path,"FABIAN-NOTICE.txt"));
        }
    }
}
