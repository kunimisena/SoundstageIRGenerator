using System.Windows.Input;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public sealed partial class MainViewModel
{
    public bool Localizing {get;private set;}
    public int LanguageIndex
    {
        get=>TextCatalog.English?1:0;
        set
        {
            if(Localizing||value is <0 or >1||value==LanguageIndex)return;
            Localizing=true;
            try
            {
                string? warning=UiLanguage.Select(value==0?"zh-CN":"en");
                LoadCards();
                foreach(var option in HeadModelNames.Concat(LayoutNames).Concat(AudioLevelModes))option.Refresh();
                foreach(string name in new[]{nameof(LanguageIndex),nameof(ReflectionTimingHint),nameof(ExcitationHint),nameof(SymmetryHint),nameof(WeightLabel),nameof(EnergySummary),nameof(GenerationNotes),nameof(ResultState),nameof(ExportState),nameof(DirectionSummary),nameof(Status),nameof(Metrics),nameof(HeadModelHint),nameof(AudioSummary),nameof(AudioToolStatus)})Notify(name);
                RefreshSpeakerLabels();if(warning!=null)SetStatus(warning);
                VisualChanged?.Invoke();
            }
            finally{Localizing=false;CommandManager.InvalidateRequerySuggested();}
        }
    }
}
