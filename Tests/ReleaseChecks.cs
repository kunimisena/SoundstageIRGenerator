using SoundstageIR.Core;
public static class ReleaseChecks
{
    public static void Run(Action<bool,string> check,string folder,string? baseline)
    {
        foreach(var p in Presets.All){var project=p.Create();project.Validate();check(project.TemplateName==p.Name,"Template provenance "+p.Name);}
        var blank=Generator.Generate(Presets.Blank.Create());check(blank.Contributions.Count==0&&blank.ReflectionPercentBeforeEq==0&&blank.Kernels.All(h=>h.All(double.IsFinite)),"Blank direct-only generation");
        var p0=Presets.BuiltIn.Single(p=>p.Name=="宽阔监听").Create();var r=Generator.Generate(p0);
        if(baseline!=null){var old=Exporter.ReadWave(baseline);check(old.SampleRate==p0.SampleRate&&old.Channels.Zip(r.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Wide monitor remains BIT-IDENTICAL to pre-change published version");}
        check(OutputNames.Safe("CON")=="_CON"&&OutputNames.Safe("../:bad? ").All(c=>!Path.GetInvalidFileNameChars().Contains(c)),"Filename sanitization and Windows reserved names");
        var a=Exporter.Export(r,folder);var b=Exporter.Export(r,folder);check(a!=b&&Path.GetFileName(a).StartsWith("宽阔监听_"),"Named exports never overwrite existing result");
        check(Directory.GetFiles(a,"*.wav").All(f=>Path.GetFileName(f).StartsWith("宽阔监听_")),"Every WAV carries template name");
        var pack=Exporter.ReadWave(TestFiles.Get(a,"Matrix_PATHS_LL_RL_LR_RR.wav"));check(pack.Channels.Zip(r.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Named export preserves matrix routes and samples");
        var c=File.ReadAllText(Path.Combine(a,ApoExporter.ConfigFileName(r.Project)));foreach(string line in c.Split('\n').Where(l=>l.StartsWith("Convolution: ")))check(File.Exists(line[13..].Trim()),"Absolute config path resolves");
        check(File.ReadAllText(Path.Combine(a,OutputNames.File(r.Project,"使用说明.txt"))).Contains("共同前导"),"General export notes retain timing and energy information");
        string language=TextCatalog.Language;
        try
        {
            foreach(string lang in new[]{"zh-CN","en"})
            {
                TextCatalog.SetLanguage(lang);
                string exported=ApoExporter.Export(r,Path.Combine(folder,lang));
                string expected="宽阔监听_"+(lang=="en"?"Equalizer_APO_Config.txt":"Equalizer_APO配置.txt");
                check(Path.GetFileName(Directory.GetFiles(exported,"*Equalizer_APO*.txt").Single())==expected,"One unmistakable import configuration "+lang);
                string guide=File.ReadAllText(Path.Combine(exported,OutputNames.File(r.Project,lang=="en"?"Import_Guide.txt":"导入说明.txt")));
                check(guide.Contains(expected)&&File.ReadAllText(Path.Combine(exported,expected)).Contains("Copy: L=LL+RL R=LR+RR"),"Localized guide names the actual routed configuration "+lang);
            }
        }
        finally { TextCatalog.SetLanguage(language); }
        var copy=ProjectIO.Deserialize(ProjectIO.Serialize(p0));check(ProjectIO.Serialize(copy)==ProjectIO.Serialize(p0),"Forward workflow state survives JSON round trip");
    }
}
