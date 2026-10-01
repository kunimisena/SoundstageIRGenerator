using System.Windows;
using System.Windows.Controls;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public partial class SimpleKernelPanel:UserControl
{
    MainViewModel? vm;Excitation? left,right;string leftStamp="",rightStamp="";bool applying;
    public SimpleKernelPanel(){InitializeComponent();DataContextChanged+=(_,_)=>Connect();Loaded+=(_,_)=>Connect();}
    public bool HasPendingEdits=>vm?.SimpleSpeaker==true&&(LeftEditor.HasChanges||(!vm.LinkSpeakerKernels&&RightEditor.HasChanges));
    void Connect(){if(vm!=null)vm.VisualChanged-=Refresh;vm=DataContext as MainViewModel;if(vm!=null)vm.VisualChanged+=Refresh;Refresh();}
    void Refresh()
    {
        if(applying||vm?.SimpleSpeaker!=true||vm.Speaker is not {} p)return;
        RightGroup.Visibility=p.LinkParameters?Visibility.Collapsed:Visibility.Visible;
        LeftEditor.ViewModel=RightEditor.ViewModel=vm;
        if(!ReferenceEquals(left,p.Left)||leftStamp!=ProjectIO.Serialize(p.Left))
        {left=p.Left;leftStamp=ProjectIO.Serialize(left);LeftEditor.BindKernel(left,e=>p.Left=e);}
        if(!ReferenceEquals(right,p.Right)||rightStamp!=ProjectIO.Serialize(p.Right))
        {right=p.Right;rightStamp=ProjectIO.Serialize(right);RightEditor.BindKernel(right,e=>p.Right=e);}
    }
    public bool CommitEdits()
    {
        if(vm?.SimpleSpeaker!=true)return true;
        applying=true;try{if(!LeftEditor.ApplyChanges()||(!vm.LinkSpeakerKernels&&!RightEditor.ApplyChanges()))return false;vm.Commit();}
        finally{applying=false;}
        Refresh();return true;
    }
    void LinkChanged(object sender,RoutedEventArgs e){CommitEdits();Refresh();vm?.PendingChanged();}
}
