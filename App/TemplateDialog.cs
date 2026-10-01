using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public sealed record TemplateSelection(Project Project,bool Generate);
public sealed class TemplateDialog:Window
{
    internal readonly MainViewModel Model=new();
    internal readonly ReflectionBatchEditor Editor=new();
    internal readonly TextBox WetPercentEditor;
    readonly StackPanel body=new(){Margin=new Thickness(20)};
    readonly TextBlock message=new(){TextWrapping=TextWrapping.Wrap};
    public TemplateSelection? Selection {get;private set;}
    public TemplateDialog(PresetCard card):this(PresetPresentation.Create(card.BuiltIn),card.Description){Model.LayoutIndex=(int)card.BuiltIn.Layout;Model.TemplateCoverage=card.BuiltIn.Layout==Distribution.Front?60:120;}
    public TemplateDialog(Project project):this(ProjectIO.Clone(project),SoundstageIR.Core.TextCatalog.T("T4153B35A52")){}
    TemplateDialog(Project project,string description)
    {
        Model.SetProject(project);Model.TemplateCount=Math.Max(2,project.DirectionCount);Model.TemplateGroup=project.TemplateName;DataContext=Model;
        Style=(Style)Application.Current.FindResource(typeof(Window));
        Title=TextCatalog.Source(project.TemplateName)+SoundstageIR.Core.TextCatalog.T("T17E01C23CB");Width=1060;Height=800;MinWidth=580;MinHeight=480;
        MaxHeight=Math.Max(480,SystemParameters.WorkArea.Height-40);MaxWidth=Math.Max(580,SystemParameters.WorkArea.Width-40);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid();root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        body.Children.Add(new TextBlock{Text=project.Name,FontSize=22,Margin=new Thickness(0,0,0,8)});
        body.Children.Add(new TextBlock{Text=TextCatalog.Source(description),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var globals=new WrapPanel();
        WetPercentEditor=AddField(globals,SoundstageIR.Core.TextCatalog.T("T82C1C0262A"),nameof(MainViewModel.ReflectionEnergyPercent));
        WetPercentEditor.ToolTip=SoundstageIR.Core.TextCatalog.T("TCA8CCCC57E");
        WetPercentEditor.SetBinding(IsEnabledProperty,new Binding(nameof(MainViewModel.ReflectionEnergyEditable)));
        var air=new CheckBox{Content=SoundstageIR.Core.TextCatalog.T("T3824A86182"),VerticalAlignment=VerticalAlignment.Center};air.SetBinding(CheckBox.IsCheckedProperty,new Binding("P.Direct.AirAbsorption"));globals.Children.Add(air);
        var distance=AddField(globals,SoundstageIR.Core.TextCatalog.T("T10EB6D2120"),"P.Direct.AirAbsorptionDistance");distance.SetBinding(IsEnabledProperty,new Binding("P.Direct.AirAbsorption"));body.Children.Add(globals);
        Editor.ViewModel=Model;body.Children.Add(Editor);Editor.LoadScope();
        Model.VisualChanged+=Editor.RefreshState;Model.CommitTemplateEdits=CommitEdits;
        body.Children.Add(message);
        var footer=new DockPanel{Margin=new Thickness(20,10,20,14)};Grid.SetRow(footer,1);root.Children.Add(footer);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);
        buttons.Children.Add(new Button{Content=SoundstageIR.Core.TextCatalog.T("T2CD0F3BE87"),IsCancel=true});
        var confirm=new Button{Content=SoundstageIR.Core.TextCatalog.T("T36F33ADAF0"),IsDefault=true};confirm.Click+=(_,_)=>Accept(false);buttons.Children.Add(confirm);
        var generate=new Button{Content=SoundstageIR.Core.TextCatalog.T("T368672781F")};generate.Click+=(_,_)=>Accept(true);buttons.Children.Add(generate);
        footer.Children.Add(new TextBlock{Text=SoundstageIR.Core.TextCatalog.T("TC15035EE01"),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,20,0)});
        Content=root;ResizePreviewHost.Install(this);Closed+=(_,_)=>Model.VisualChanged-=Editor.RefreshState;
    }
    static TextBox AddField(Panel row,string label,string property)
    {
        var group=new StackPanel{Margin=new Thickness(0,0,18,0),MinWidth=140};row.Children.Add(group);group.Children.Add(new TextBlock{Text=label});
        var box=new TextBox();box.SetBinding(TextBox.TextProperty,new Binding(property){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true});group.Children.Add(box);return box;
    }
    bool CommitEdits()
    {
        EditorInput.Commit(body);
        if(EditorInput.HasErrors(body)){message.Text=SoundstageIR.Core.TextCatalog.T("TB469D01528");return false;}
        return Editor.ApplyChanges();
    }
    void Accept(bool generate){if(CreateSelection(generate)!=null)DialogResult=true;}
    internal TemplateSelection? CreateSelection(bool generate)
    {
        try
        {
            if(!CommitEdits())return null;
            EditingLimits.Normalize(Model.P);Model.P.Validate();Selection=new(ProjectIO.Clone(Model.P),generate);return Selection;
        }
        catch(Exception ex){message.Text=TextCatalog.Diagnostic(ex.Message);return null;}
    }
}
