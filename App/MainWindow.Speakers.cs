using System.Windows;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
namespace SoundstageIRGenerator;
public partial class MainWindow
{
    void InitializeSpeakerWindow()
    {
        if(!VM.IsSpeaker)return;
        Title+=" · Speakers";Closed+=(_,_)=>VM.StopSpeakerPrecheck();
        _=VM.UpdateSpeakerPrecheckAsync();
        void Refresh(){PresetIntro.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty,VM.SimpleSpeaker?"Speaker.SimpleHint":"T00DBD9D3F4");DirectControls.SetResourceReference(System.Windows.Controls.HeaderedContentControl.HeaderProperty,VM.SimpleSpeaker?"Speaker.SimpleDirect":"Speaker.TargetDirect");}
        VM.VisualChanged+=Refresh;Refresh();
    }
    TemplateSelection? RequestProjectTemplate(PresetCard card)
    {
        if(VM.SimpleSpeaker)
        {
            var p=SpeakerProject.FromPreset(card.BuiltIn,SpeakerMode.SimpleReverb,PresetPresentation.Create(card.BuiltIn));
            var dialog=new SimpleSpeakerTemplateDialog(p){Owner=this};return dialog.ShowDialog()==true?dialog.Selection:null;
        }
        var original=new TemplateDialog(card,allowGenerate:!VM.SpatialSpeaker){Owner=this};return original.ShowDialog()==true?original.Selection:null;
    }
    async void EditSimpleTemplate()
    {
        var dialog=new SimpleSpeakerTemplateDialog(ProjectIO.Clone(VM.Speaker!)){Owner=this};
        if(dialog.ShowDialog()==true&&dialog.Selection is {} selection){VM.SetSpeakerProject(dialog.Project);ShowConfiguration();if(selection.Generate)await VM.GenerateAsync();}
    }
    void ChangeSpeakerMode(object sender,RoutedEventArgs e)
    {
        if(!CommitConfiguration())return;
        var dialog=new SpeakerModeDialog{Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        if(dialog.ShowDialog()==true&&dialog.Selection is {} mode&&mode!=VM.Speaker!.Mode)
        {var p=ProjectIO.Clone(VM.Speaker);p.Mode=mode;VM.SetSpeakerProject(p);Pages.SelectedItem=PresetPage;}
    }
}
