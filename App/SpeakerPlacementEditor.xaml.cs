using System.Windows;
using System.Windows.Controls;
namespace SoundstageIRGenerator;
public partial class SpeakerPlacementEditor:UserControl
{
    public SpeakerPlacementEditor(){InitializeComponent();}
    void Edited(object sender,RoutedEventArgs e){EditorInput.Commit(this);if(!EditorInput.HasErrors(this))(DataContext as MainViewModel)?.Commit();}
    void Mirror(object sender,RoutedEventArgs e){Edited(sender,e);if(!EditorInput.HasErrors(this))(DataContext as MainViewModel)?.MirrorSpeakerPosition();}
}
