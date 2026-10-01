using System.Windows;
using System.Windows.Controls;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
namespace SoundstageIRGenerator;
public partial class SpeakerModeDialog:Window
{
    public SpeakerMode? Selection {get;private set;}
    public SpeakerModeDialog(){InitializeComponent();ModeLanguage.SelectedIndex=TextCatalog.English?1:0;}
    void LanguageChanged(object sender,SelectionChangedEventArgs e){if(ModeLanguage.SelectedIndex>=0)UiLanguage.Select(ModeLanguage.SelectedIndex==0?"zh-CN":"en");}
    void ChooseSimple(object sender,RoutedEventArgs e){Selection=SpeakerMode.SimpleReverb;DialogResult=true;}
    void ChooseSpatial(object sender,RoutedEventArgs e){Selection=SpeakerMode.SpatialField;DialogResult=true;}
}
