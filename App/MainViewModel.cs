using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;

public sealed class ActionCommand(Action<object?> action, Func<bool>? can=null):ICommand
{
    public bool CanExecute(object? p)=>can?.Invoke()??true;
    public void Execute(object? p)=>action(p);
    public event EventHandler? CanExecuteChanged {add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value;}
}
public sealed record PresetCard(string Name,string Description,string Details,Preset BuiltIn);
public sealed partial class MainViewModel:INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? VisualChanged;
    public event Action? PresetApplied;
    public Func<PresetCard,TemplateSelection?>? RequestTemplate {get;set;}
    public string ReflectionTimingHint=>Editing is {} e ? $"首反相对直达 +{e.OnsetMs:0.##} ms；约 {e.BuildMs:0.##} ms 内由疏变密。" : "";
    public Func<bool>? CommitTemplateEdits {get;set;}
    public Func<bool>? HasTemplateEdits {get;set;}
    public Project TemplateProject {get {var p=ProjectIO.Clone(P);p.Sources=ProjectIO.Clone(P.TemplateSources);return p;}}
    bool pendingEdits;
    public void PendingChanged()
    {
        bool next=HasTemplateEdits?.Invoke()??false;if(next==pendingEdits)return;pendingEdits=next;
        Notify(nameof(Stale));Notify(nameof(ResultState));Notify(nameof(ExportState));Notify(nameof(CanExport));CommandManager.InvalidateRequerySuggested();
    }
    public AnalysisCache AnalysisCache {get;}=new();
    void Notify([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new(n));
    public string Root {get;}
    public Project P {get;private set;}=Presets.BuiltIn.Single(p=>p.Name=="宽阔监听").Create();
    public ObservableCollection<ReflectionPair> Sources {get;}=[];
    public ObservableCollection<PresetCard> PresetCards {get;}=[];
    public List<ReflectionPair> Selection {get;set;}=[];
    ReflectionPair? selected;
    public ReflectionPair? Selected {get=>selected;set{selected=value;Notify("");VisualChanged?.Invoke();}}
    int excitationIndex;
    public int ExcitationIndex {get=>excitationIndex;set{excitationIndex=value;Notify("");VisualChanged?.Invoke();}}
    public Excitation? Editing=>Selected==null?null:ExcitationIndex==0?Selected.Left:Selected.AutoRight||Selected.Median?Selected.EffectiveRight():Selected.Right;
    public bool CanEditExcitation=>Selected!=null&&(ExcitationIndex==0||(!Selected.AutoRight&&!Selected.Median));
    public string ExcitationHint=>ExcitationIndex==0?"L 输入 · 当前方向的统计核":Selected?.Median==true?"对称面：两输入共享参数":Selected?.AutoRight==true?"R 输入 · 从 L 参数自动衍生（预览）":"R 输入 · 独立编辑";
    public string SymmetryHint=>P.StrictMirror?"参数镜像 · 随机细节镜像 · 一个共同 EQ":"参数镜像 · 随机细节独立 · 两级 EQ";
    public bool DirectEnabled
    {
        get=>P.Direct.Enabled;
        set {if(Busy||value==P.Direct.Enabled)return;P.Direct.Enabled=value;Commit();}
    }
    public bool ReflectionEnergyEditable=>Ready&&DirectEnabled;
    // Keep the requested balance in the project so re-enabling direct sound restores it.
    public double ReflectionEnergyPercent
    {
        get=>DirectEnabled?P.ReflectionEnergyPercent:100;
        set
        {
            if(!ReflectionEnergyEditable||value==P.ReflectionEnergyPercent)return;
            P.ReflectionEnergyPercent=value;Notify();
        }
    }
    public bool EarEqEditable=>Ready&&P.Equalize;
    public bool CenterEqEditable=>Ready&&P.Equalize&&!P.StrictMirror;
    public double EarEqStrengthPercent
    {
        get=>P.EarEqStrengthPercent;
        set {double v=EditingLimits.Clamp(value,0,100);if(Busy||v==P.EarEqStrengthPercent)return;P.EarEqStrengthPercent=v;Commit();}
    }
    public double CenterEqStrengthPercent
    {
        get=>P.CenterEqStrengthPercent;
        set {double v=EditingLimits.Clamp(value,0,100);if(Busy||v==P.CenterEqStrengthPercent)return;P.CenterEqStrengthPercent=v;Commit();}
    }
    public string WeightLabel=>"源相对权重 dB";
    public string EnergySummary=>Result==null?"":$"混响能量：EQ 前 {Result.ReflectionPercentBeforeEq:0.0}% → EQ 后 {Result.ReflectionPercentAfterEq:0.0}%"+(Result.Warnings.Count>0?$" · {Result.Warnings.Count} 条生成提示（悬停查看）":"");
    public string GenerationNotes=>Result==null?"":string.Join("\n",Result.Warnings);
    public bool Busy {get;private set;}
    public bool Ready=>!Busy;
    public bool Generating {get;private set;}
    public bool CanExport=>Ready&&Result!=null&&!Stale;
    bool stale=true;
    public bool Stale {get=>stale||pendingEdits;private set=>stale=value;}
    public string ResultState=>Generating?"正在生成…":Result==null?"尚未生成":Stale?"参数已修改 · 图表待更新":"已生成 · 当前参数";
    public string ExportState=>Generating?"正在生成卷积核，完成后即可导出。":Busy?"正在处理，请稍候。":Result==null?"尚未生成卷积核。点击“生成卷积核”，完成后即可导出。":Stale?"卷积核与编辑参数不一致。请生成卷积核后导出。":$"{Result.Project.Name} · 卷积核与当前参数一致，可以导出。";
    public string DirectionSummary=>$"{P.DirectionCount} 个方向 / {Sources.Count} 个可编辑源";
    public string Status {get;private set;}="选择模板开始，或打开已有配置。";
    public double Progress {get;private set;}
    public GenerationResult? Result {get;private set;}
    public string Metrics=>Result==null?"":$"{Result.Duration:F3} s  ·  {Result.Project.SampleRate/1000.0:G} kHz  ·  计算 {Result.Seconds:F2} s\n{HeadRenderer.Description(Result.Project)}\n混响分量能量：EQ 前 {Result.ReflectionPercentBeforeEq:F2}% / EQ 后 {Result.ReflectionPercentAfterEq:F2}%\n共同标定 {Result.CommonGainDb:+0.00;-0.00;0} dB  ·  前导 {Result.ZeroSample:F0} 样本\n参考电平 {Result.OutputReferenceDb:F2} dB\n{Result.EqResidualReference}：平滑残差 RMS {Result.EqResidualDb:F2} dB  ·  频点峰值上界 {Result.MaxBinGainDb:F1} dB\n峰值增益保守上界 {Result.PeakBoundDb:F1} dB\n"+string.Join("\n",Result.EqReports.Select(e=>$"{e.Stage}：增益范围 {e.MaximumCutDb:F1}～{e.MaximumBoostDb:F1} dB · {e.Taps} 点 · 设计偏差 {e.DesignErrorDb:F3} dB"))+"\n"+GenerationNotes;
    public string ExportLabel=>OutputNames.Label(P);
    public string SuggestedProjectFileName=>ExportLabel+".json";
    public string ExportParent {get;set;}
    public string LastExport {get;private set;}="";
    public string ProjectFile {get;private set;}="";
    public string[] HeadModelNames {get;}=["球形头 · 遮挡与时差","FABIAN · 耳廓、头与肩胸部"];
    public int HeadModelIndex
    {
        get=>(int)P.HeadModel;
        set {if(value<0||value>1||Busy||value==(int)P.HeadModel)return;P.HeadModel=(HeadModelKind)value;Commit();}
    }
    public bool SphereHeadSelected=>P.HeadModel==HeadModelKind.Sphere;
    public bool FabianHeadSelected=>P.HeadModel==HeadModelKind.Fabian;
    public string HeadModelHint=>FabianHeadSelected
        ? "直达与反射均使用 FABIAN。约 2° 方向网格；保留幅相和耳间时差。解剖响应始终镜像，随机镜像开关只控制反射细节。"
        : "球形头的遮挡与时差，可调半径和遮挡强度。";
    public int[] SampleRates {get;}=[44100,48000,96000];
    public string[] LayoutNames {get;}=["水平环绕","球形环绕","上半球","前向扇区","镜像成对"];
    public int LayoutIndex {get;set;}
    public int TemplateCount {get;set;}=8;
    public double TemplateYaw {get;set;}
    public double TemplateElevation {get;set;}
    public double TemplateCoverage {get;set;}=90;
    public double TemplateEnergy {get;set;}=0;
    public string TemplateGroup {get;set;}="新反射组";
    public bool TemplateReplace {get;set;}=true;
    readonly Stack<string> undo=[];readonly Stack<string> redo=[];
    string snapshot="";ReflectionPair? clipboard;
    CancellationTokenSource? cancellation;
    public ICommand GenerateCommand {get;}
    public ICommand CancelCommand {get;}
    public ICommand CopyOutputTextCommand {get;}
    public ICommand ExportCommand {get;}
    public ICommand SaveCommand {get;}
    public ICommand OpenCommand {get;}
    public ICommand PresetCommand {get;}
    public ICommand UndoCommand {get;}
    public ICommand RedoCommand {get;}
    public ICommand TemplateCommand {get;}
    public ICommand DuplicateCommand {get;}
    public ICommand NewSourceCommand {get;}
    public ICommand DeleteCommand {get;}
    public ICommand CopyCommand {get;}
    public ICommand PasteCommand {get;}
    public ICommand BatchCommand {get;}
    public ICommand SeedCommand {get;}
    public MainViewModel()
    {
        Root=FindRoot();InitializeAudio();ExportParent=Path.Combine(Root,"exports");RefreshSources();snapshot=ProjectIO.Serialize(P);LoadCards();
        GenerateCommand=new ActionCommand(async _=>await GenerateAsync(),()=>Ready);
        CancelCommand=new ActionCommand(_=>cancellation?.Cancel(),()=>Busy);
        CopyOutputTextCommand=new ActionCommand(text=>{if(text is string value&&!string.IsNullOrEmpty(value))Safe(()=>Clipboard.SetText(value));});
        ExportCommand=new ActionCommand(async _=>await ExportAsync(),()=>CanExport);
        SaveCommand=new ActionCommand(_=>Safe(SaveDialog),()=>Ready);
        OpenCommand=new ActionCommand(_=>Safe(OpenDialog),()=>Ready);
        PresetCommand=new ActionCommand(async o=>
        {
            try
            {
                if(o is not PresetCard card)return;
                var choice=RequestTemplate?.Invoke(card);
                if(choice==null)return;
                await ApplyTemplateAsync(choice);
            }
            catch(Exception ex){SetStatus(ex.Message);}
        },()=>Ready);
        UndoCommand=new ActionCommand(_=>Undo(),()=>Ready&&(undo.Count>0||pendingEdits));
        RedoCommand=new ActionCommand(_=>Redo(),()=>Ready&&redo.Count>0);
        TemplateCommand=new ActionCommand(_=>Safe(GenerateTemplate),()=>Ready);
        NewSourceCommand=new ActionCommand(_=>Safe(()=>
        {
            if(P.Sources.Sum(s=>s.Multiplicity)>30)throw new InvalidOperationException("最多 32 个方向。");
            var s=new ReflectionPair{Name="新反射源",Group="自定义"};P.Sources.Add(s);Commit();RefreshSources();Selected=s;
        }),()=>Ready);
        DuplicateCommand=new ActionCommand(_=>Safe(Duplicate),()=>Ready&&Selected!=null);
        DeleteCommand=new ActionCommand(_=>{foreach(var s in Targets())P.Sources.Remove(s);Commit();RefreshSources();},()=>Ready&&Selected!=null);
        CopyCommand=new ActionCommand(_=>{clipboard=ProjectIO.Clone(Selected!);SetStatus("参数已复制；粘贴时保留目标方向并分配新随机实现。");},()=>Selected!=null);
        PasteCommand=new ActionCommand(_=>Paste(true),()=>Ready&&clipboard!=null&&Selected!=null);
        BatchCommand=new ActionCommand(_=>{clipboard=ProjectIO.Clone(Selected!);Paste(false);},()=>Ready&&Selected!=null&&Selection.Count>1);
        SeedCommand=new ActionCommand(_=>{P.Seed=Random.Shared.Next(1,int.MaxValue);Commit();},()=>Ready);
    }
    public async Task ApplyTemplateAsync(TemplateSelection selection)
    {
        if(Busy)return;
        SetProject(selection.Project);SetStatus("已载入 "+P.Name);PresetApplied?.Invoke();
        if(selection.Generate)await GenerateAsync();
    }
    static string FindRoot()
    {
        if(App.TestRoot!=null)return App.TestRoot;
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.txt")))return AppContext.BaseDirectory;
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir!=null){if(Directory.Exists(Path.Combine(dir.FullName,"Core"))&&Directory.Exists(Path.Combine(dir.FullName,"App")))return dir.FullName;dir=dir.Parent;}
        return AppContext.BaseDirectory;
    }
    public void ApplyReflectionEdit(ReflectionEditSession edit)
    {
        if(Busy)throw new InvalidOperationException("请等待当前计算结束。");
        var next=edit.Apply(TemplateProject);next.TemplateSources=ProjectIO.Clone(next.Sources);var selectedIds=Selection.Select(s=>s.Id).ToHashSet();var selectedId=Selected?.Id;
        P=next;Commit();RefreshSources();Selection=Sources.Where(s=>selectedIds.Contains(s.Id)).ToList();
        Selected=Sources.FirstOrDefault(s=>s.Id==selectedId)??Sources.FirstOrDefault();
        SetStatus("整体调整已应用，可撤销。"+(edit.ClampedValues>0?" 部分越界数值已钳位。":"")+(edit.CurveSimplified?" 复杂曲线已在 256 点内近似。":""));
    }
    public void SetStatus(string s){Status=s;Notify(nameof(Status));}
    public void Commit()
    {
        int clamped=EditingLimits.Normalize(P);if(clamped>0)SetStatus("超出计算范围的数值已钳位；其余修改保留。");
        string next=ProjectIO.Serialize(P);if(next==snapshot)return;undo.Push(snapshot);redo.Clear();snapshot=next;
        RefreshResultIdentity();
        if(!string.IsNullOrEmpty(LastExport))Status=stale?"参数已修改，请重新生成。":"配置名称已更新，可以直接导出。";
        LastExport="";
        Notify("");VisualChanged?.Invoke();CommandManager.InvalidateRequerySuggested();
    }
    void RefreshResultIdentity()
    {
        if(Result==null){Stale=true;return;}
        var compare=ProjectIO.Clone(P);compare.Name=Result.Project.Name;compare.TemplateName=Result.Project.TemplateName;
        Stale=ProjectIO.Serialize(compare)!=ProjectIO.Serialize(Result.Project);
        if(!stale){Result.Project.Name=P.Name;Result.Project.TemplateName=P.TemplateName;}
    }
    public void SetProject(Project p)
    {undo.Push(snapshot);redo.Clear();P=p;Result=null;LastExport="";pendingEdits=false;AnalysisCache.Clear();ProjectFile="";snapshot=ProjectIO.Serialize(P);Stale=true;RefreshSources();Notify("");VisualChanged?.Invoke();}
    public void Undo(){if(CommitTemplateEdits?.Invoke()==false||undo.Count==0)return;redo.Push(snapshot);Restore(undo.Pop());}
    public void Redo(){if(CommitTemplateEdits?.Invoke()==false||redo.Count==0)return;undo.Push(snapshot);Restore(redo.Pop());}
    void Restore(string s){snapshot=s;P=ProjectIO.Deserialize(s);LastExport="";RefreshResultIdentity();RefreshSources();Notify("");VisualChanged?.Invoke();}
    void RefreshSources()
    {
        Guid? id=Selected?.Id;Sources.Clear();foreach(var s in P.Sources)Sources.Add(s);Selection=[];
        Selected=Sources.FirstOrDefault(s=>s.Id==id)??Sources.FirstOrDefault();
    }
    List<ReflectionPair> Targets()=>Selection.Count>0?Selection.ToList():Selected==null?[]:[Selected];
    void Duplicate()
    {
        var add=Targets().Select(s=>s.CloneIndependent()).ToList();if(P.Sources.Sum(s=>s.Multiplicity)+add.Sum(s=>s.Multiplicity)>32)throw new InvalidOperationException("最多 32 个方向。");
        P.Sources.AddRange(add);Commit();RefreshSources();Selected=add.FirstOrDefault();
    }
    void Paste(bool independent)
    {
        if(clipboard==null)return;
        foreach(var s in Targets())
        {
            if(!independent&&s==Selected)continue;
            s.Left=clipboard.Left.Clone();s.Right=clipboard.Right.Clone();s.AutoRight=clipboard.AutoRight;s.RightGainDb=clipboard.RightGainDb;s.RightTilt=clipboard.RightTilt;s.Group=clipboard.Group;
            if(independent)s.Id=Guid.NewGuid();
        }
        Commit();RefreshSources();
    }
    public void GenerateTemplate()
    {
        if(CommitTemplateEdits?.Invoke()==false)return;
        if(TemplateCount<2||TemplateCount>32||TemplateCount%2!=0)throw new InvalidOperationException("模板方向数请输入 2–32 之间的偶数。");
        var e=P.TemplateSources.FirstOrDefault()?.Left.Clone()??new Excitation();e.GainDb=TemplateEnergy;
        var generated=Templates.Create((Distribution)LayoutIndex,TemplateCount,TemplateYaw,TemplateElevation,TemplateCoverage,e,TemplateGroup);
        if(!TemplateReplace&&P.TemplateSources.Sum(s=>s.Multiplicity)+TemplateCount>32)throw new InvalidOperationException("加入后超过 32 个方向。");
        if(TemplateReplace)P.TemplateSources.Clear();P.TemplateSources.AddRange(generated);P.Sources=ProjectIO.Clone(P.TemplateSources);Commit();RefreshSources();
    }
    public async Task<bool> GenerateAsync()
    {
        if(Busy)return false;
        try
        {
            if(CommitTemplateEdits?.Invoke()==false)return false;
            Commit();P.Validate();var input=ProjectIO.Clone(P);var version=snapshot;Busy=true;Generating=true;Progress=0;Notify("");CommandManager.InvalidateRequerySuggested();
            cancellation=new();var progress=new Progress<(double Fraction,string Message)>(v=>{Progress=v.Fraction*100;Status=v.Message;Notify(nameof(Progress));Notify(nameof(Status));});
            var r=await Task.Run(()=>Generator.Generate(input,progress,cancellation.Token));Result=r;Stale=snapshot!=version;Status="卷积核已生成。";return true;
        }
        catch(OperationCanceledException){Status="生成已取消。";return false;}
        catch(Exception e){Status="生成失败："+e.Message;return false;}
        finally{Generating=false;Busy=false;cancellation?.Dispose();cancellation=null;Notify("");VisualChanged?.Invoke();CommandManager.InvalidateRequerySuggested();}
    }
    public void Cancel()=>cancellation?.Cancel();
    public async Task<string?> ExportAsync()
    {
        if(Busy||CommitTemplateEdits?.Invoke()==false)return null;Commit();if(Result==null||Stale){SetStatus("参数已变化，请先重新生成。");return null;}
        try{Busy=true;Notify("");var r=Result;string path=ExportParent;LastExport=await Task.Run(()=>Exporter.Export(r,path));Status="已导出到 "+LastExport;return LastExport;}
        catch(Exception e){Status="导出失败："+e.Message;return null;}
        finally{Busy=false;Notify("");CommandManager.InvalidateRequerySuggested();}
    }
    void SaveDialog()
    {
        if(CommitTemplateEdits?.Invoke()==false)return;
        Commit();P.Validate();Directory.CreateDirectory(Path.Combine(Root,"projects"));
        var dialog=new SaveFileDialog{Title="导出配置",Filter="声场配置 (*.json)|*.json",InitialDirectory=Path.Combine(Root,"projects"),FileName=SuggestedProjectFileName};
        if(dialog.ShowDialog()==true){ProjectIO.Save(P,dialog.FileName);ProjectFile=dialog.FileName;SetStatus("项目已保存："+ProjectFile);}
    }
    void OpenDialog(){var d=new OpenFileDialog{Title="导入配置",Filter="声场配置 (*.json)|*.json",InitialDirectory=Path.Combine(Root,"projects")};if(d.ShowDialog()==true){SetProject(ProjectIO.Load(d.FileName));ProjectFile=d.FileName;SetStatus("已打开 "+ProjectFile);PresetApplied?.Invoke();}}
    void LoadCards()
    {
        PresetCards.Clear();
        foreach(var p in Presets.All)PresetCards.Add(new(p.Name,p.Description,p.Details,p));
    }
    void Safe(Action action){try{action();}catch(Exception e){SetStatus(e.Message);}}
}
