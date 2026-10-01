using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
namespace SoundstageIRGenerator;
public sealed class SimpleSpeakerTemplateDialog:Window
{
    readonly MainViewModel vm=new();readonly SimpleKernelPanel editor=new();readonly StackPanel body=new(){Margin=new Thickness(20)};readonly TextBlock error=new();
    public SpeakerProject Project=>vm.Speaker!;
    public TemplateSelection? Selection {get;private set;}
    public SimpleSpeakerTemplateDialog(SpeakerProject p)
    {
        vm.SetSpeakerProject(p);DataContext=vm;Style=(Style)Application.Current.FindResource(typeof(Window));Title=p.Field.Name;
        Width=880;Height=760;MinWidth=580;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid();root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        body.Children.Add(new TextBlock{Text=p.Field.Name,FontSize=22,Margin=new Thickness(0,0,0,12)});
        var label=new TextBlock();label.SetResourceReference(TextBlock.TextProperty,"T82C1C0262A");body.Children.Add(label);
        var wet=new TextBox();wet.SetBinding(TextBox.TextProperty,new Binding("ReflectionEnergyPercent"){Mode=BindingMode.TwoWay,ValidatesOnExceptions=true});body.Children.Add(wet);
        editor.DataContext=vm;body.Children.Add(editor);body.Children.Add(error);
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(20,10,20,14)};Grid.SetRow(footer,1);root.Children.Add(footer);
        var cancel=new Button{IsCancel=true};cancel.SetResourceReference(ContentControl.ContentProperty,"T2CD0F3BE87");footer.Children.Add(cancel);
        foreach(var (key,generate) in new[]{("T36F33ADAF0",false),("T368672781F",true)}){var b=new Button();b.SetResourceReference(ContentControl.ContentProperty,key);b.Click+=(_,_)=>{if(Accept(generate))DialogResult=true;};footer.Children.Add(b);}
        Content=root;ResizePreviewHost.Install(this);
    }
    public bool Accept(bool generate)
    {
        try{EditorInput.Commit(body);if(EditorInput.HasErrors(body)||!editor.CommitEdits())return false;Project.Validate();
            // The existing template selection carries a source profile into ApplyTemplateProject.
            var field=ProjectIO.Clone(Project.Field);var source=new ReflectionPair{Left=Project.Left.Clone(),Right=Project.Right.Clone()};field.Sources=[source];field.TemplateSources=[source];
            Selection=new(field,generate,ProjectIO.Clone(Project));return true;
        }catch(Exception e){error.Text=e.Message;return false;}
    }
}
