using System.ComponentModel;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
// Stable items keep SelectedIndex and model values intact when labels change.
public sealed class LocalizedOption(string key):INotifyPropertyChanged
{
    public string Label=>TextCatalog.T(key);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()=>PropertyChanged?.Invoke(this,new(nameof(Label)));
    public override string ToString()=>Label;
}
public static class PresetPresentation
{
    public static Project Create(Preset preset)
    {
        var p=preset.Create();
        if(!TextCatalog.English)return p;
        p.Name=TextCatalog.Source(p.Name);p.TemplateName=TextCatalog.Source(p.TemplateName);
        foreach(var s in p.Sources.Concat(p.TemplateSources))
        {
            s.Group=TextCatalog.Source(s.Group);
            s.Name=System.Text.RegularExpressions.Regex.Replace(s.Name,@"^(前侧|后侧|侧向|上方)(?= \d+)",m=>TextCatalog.Source(m.Value));
        }
        return p;
    }
}
