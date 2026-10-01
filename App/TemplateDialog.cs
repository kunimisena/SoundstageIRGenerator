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
    public TemplateDialog(PresetCard card):this(card.BuiltIn.Create(),card.Description){Model.LayoutIndex=(int)card.BuiltIn.Layout;Model.TemplateCoverage=card.BuiltIn.Layout==Distribution.Front?60:120;}
    public TemplateDialog(Project project):this(ProjectIO.Clone(project),"调整模板的整体曲线与方向分布。"){}
    TemplateDialog(Project project,string description)
    {
        Model.SetProject(project);Model.TemplateCount=Math.Max(2,project.DirectionCount);Model.TemplateGroup=project.TemplateName;DataContext=Model;
        Style=(Style)Application.Current.FindResource(typeof(Window));
        Title=project.TemplateName+" · 模板参数";Width=1060;Height=800;MinWidth=580;MinHeight=480;
        MaxHeight=Math.Max(480,SystemParameters.WorkArea.Height-40);MaxWidth=Math.Max(580,SystemParameters.WorkArea.Width-40);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid();root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        body.Children.Add(new TextBlock{Text=project.Name,FontSize=22,Margin=new Thickness(0,0,0,8)});
        body.Children.Add(new TextBlock{Text=description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var globals=new WrapPanel();
        WetPercentEditor=AddField(globals,"混响能量占比 / %",nameof(MainViewModel.ReflectionEnergyPercent));
        WetPercentEditor.ToolTip="目标为 EQ 与带通后的混响分量占比；通过整体混响增益预修正保持。";
        WetPercentEditor.SetBinding(IsEnabledProperty,new Binding(nameof(MainViewModel.ReflectionEnergyEditable)));
        var air=new CheckBox{Content="直达声空气吸收",VerticalAlignment=VerticalAlignment.Center};air.SetBinding(CheckBox.IsCheckedProperty,new Binding("P.Direct.AirAbsorption"));globals.Children.Add(air);
        var distance=AddField(globals,"空气吸收距离 / m","P.Direct.AirAbsorptionDistance");distance.SetBinding(IsEnabledProperty,new Binding("P.Direct.AirAbsorption"));body.Children.Add(globals);
        Editor.ViewModel=Model;body.Children.Add(Editor);Editor.LoadScope();
        Model.VisualChanged+=Editor.RefreshState;Model.CommitTemplateEdits=CommitEdits;
        body.Children.Add(message);
        var footer=new DockPanel{Margin=new Thickness(20,10,20,14)};Grid.SetRow(footer,1);root.Children.Add(footer);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);
        buttons.Children.Add(new Button{Content="取消",IsCancel=true});
        var confirm=new Button{Content="确认",IsDefault=true};confirm.Click+=(_,_)=>Accept(false);buttons.Children.Add(confirm);
        var generate=new Button{Content="确认并生成卷积核"};generate.Click+=(_,_)=>Accept(true);buttons.Children.Add(generate);
        footer.Children.Add(new TextBlock{Text="修改整体曲线或方向分布后，按模板重新生成反射源。",TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,20,0)});
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
        if(EditorInput.HasErrors(body)){message.Text="请检查红框内的数值。";return false;}
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
        catch(Exception ex){message.Text=ex.Message;return null;}
    }
}
