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
    public string ReflectionTimingHint=>Editing is {} e ? SoundstageIR.Core.TextCatalog.F("TCA9D17AE42", e.OnsetMs, e.BuildMs) : "";
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
    public Project P {get;private set;}=PresetPresentation.Create(Presets.BuiltIn.Single(p=>p.Name=="宽阔监听"));
    public ObservableCollection<ReflectionPair> Sources {get;}=[];
    public ObservableCollection<PresetCard> PresetCards {get;}=[];
    public List<ReflectionPair> Selection {get;set;}=[];
    ReflectionPair? selected;
    public ReflectionPair? Selected {get=>selected;set{selected=value;Notify("");VisualChanged?.Invoke();}}
    int excitationIndex;
    public int ExcitationIndex {get=>excitationIndex;set{excitationIndex=value;Notify("");VisualChanged?.Invoke();}}
    public Excitation? Editing=>Selected==null?null:ExcitationIndex==0?Selected.Left:Selected.AutoRight||Selected.Median?Selected.EffectiveRight():Selected.Right;
    public bool CanEditExcitation=>Selected!=null&&(ExcitationIndex==0||(!Selected.AutoRight&&!Selected.Median));
    public string ExcitationHint=>ExcitationIndex==0?SoundstageIR.Core.TextCatalog.T("T0379C8AF73"):Selected?.Median==true?SoundstageIR.Core.TextCatalog.T("T4135DA672E"):Selected?.AutoRight==true?SoundstageIR.Core.TextCatalog.T("T57AFA93579"):SoundstageIR.Core.TextCatalog.T("TA7D48C3CB0");
    public string SymmetryHint=>P.StrictMirror?SoundstageIR.Core.TextCatalog.T("T70C5A189ED"):SoundstageIR.Core.TextCatalog.T("T4927B3F9BD");
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
    public bool CenterEqEditable=>Ready&&P.Equalize&&!P.StrictMirror&&!SimpleSpeaker;
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
    public string WeightLabel=>SoundstageIR.Core.TextCatalog.T("T57400350D5");
    public string EnergySummary=>SpeakerResult!=null?SpeakerEnergySummary:Result==null?"":SoundstageIR.Core.TextCatalog.F("TA1E21B6668", (Result.Project.Direct.Enabled?Result.Project.ReflectionEnergyPercent:100), Result.ReflectionPercentAfterEq)+(Result.Warnings.Count>0?SoundstageIR.Core.TextCatalog.F("T4E7A1F9406", Result.Warnings.Count):"");
    public string GenerationNotes=>SpeakerResult!=null?string.Join("\n",SpeakerResult.Warnings.Select(TextCatalog.Diagnostic)):Result==null?"":string.Join("\n",Result.Warnings.Select(TextCatalog.Diagnostic));
    public bool Busy {get;private set;}
    public bool Ready=>!Busy;
    public bool Generating {get;private set;}
    public bool CanExport=>Ready&&Result!=null&&!Stale;
    bool stale=true;
    public bool Stale {get=>stale||pendingEdits;private set=>stale=value;}
    public string ResultState=>Generating?SoundstageIR.Core.TextCatalog.T("TA8B8730C96"):Result==null?SoundstageIR.Core.TextCatalog.T("TD5638D161F"):Stale?SoundstageIR.Core.TextCatalog.T("TE1BA3603B5"):SoundstageIR.Core.TextCatalog.T("T298BC14648");
    public string ExportState=>Generating?SoundstageIR.Core.TextCatalog.T("T6CFC7BCBFE"):Busy?SoundstageIR.Core.TextCatalog.T("T6E15B35025"):Result==null?SoundstageIR.Core.TextCatalog.T("T4145E1FB25"):Stale?SoundstageIR.Core.TextCatalog.T("T9964066893"):SoundstageIR.Core.TextCatalog.F("T12AB49F98A", Result.Project.Name);
    public string DirectionSummary=>SoundstageIR.Core.TextCatalog.F("TDE2675EE93", P.DirectionCount, Sources.Count);
    string rawStatus=SoundstageIR.Core.TextCatalog.T("T3573E79B8C");
    public string Status {get=>TextCatalog.Diagnostic(rawStatus);private set=>rawStatus=value;}
    public double Progress {get;private set;}
    public GenerationResult? Result {get;private set;}
    public string Metrics=>SpeakerResult!=null?SpeakerMetrics:Result==null?"":SoundstageIR.Core.TextCatalog.F("T136BC05279", Result.Duration, Result.Project.SampleRate/1000.0, Result.Seconds, TextCatalog.Diagnostic(HeadRenderer.Description(Result.Project)), (Result.Project.Direct.Enabled?Result.Project.ReflectionEnergyPercent:100), Result.ReflectionPercentBeforeEq, Result.ReflectionPercentAfterEq, Result.WetBalance.GainDb, Result.WetBalance.Evaluations, Result.CommonGainDb, Result.ZeroSample, Result.OutputReferenceDb, TextCatalog.Diagnostic(Result.EqResidualReference), Result.EqResidualDb, Result.MaxBinGainDb, Result.PeakBoundDb, Result.ProjectionErrorDb, Result.DiscardedEnergyDb)+string.Join("\n",Result.EqReports.Select(e=>SoundstageIR.Core.TextCatalog.F("T076E536AA3", TextCatalog.Diagnostic(e.Stage), e.MaximumCutDb, e.MaximumBoostDb, e.Taps, e.ResponseResidualDb)))+"\n"+GenerationNotes;
    public string ExportLabel=>OutputNames.Label(P);
    public string SuggestedProjectFileName=>ExportLabel+".json";
    public string ExportParent {get;set;}
    public string LastExport {get;private set;}="";
    public string ProjectFile {get;private set;}="";
    public LocalizedOption[] HeadModelNames {get;}=[new("TB5C9ED8404"), new("T4E25C361CA")];
    public int HeadModelIndex
    {
        get=>(int)P.HeadModel;
        set {if(Localizing||value<0||value>1||Busy||value==(int)P.HeadModel)return;P.HeadModel=(HeadModelKind)value;Commit();}
    }
    public bool SphereHeadSelected=>P.HeadModel==HeadModelKind.Sphere;
    public bool FabianHeadSelected=>P.HeadModel==HeadModelKind.Fabian;
    public string HeadModelHint=>FabianHeadSelected
        ? SoundstageIR.Core.TextCatalog.T("T9C32D627E1")
        : SoundstageIR.Core.TextCatalog.T("TCC55B89955");
    public int[] SampleRates {get;}=[44100,48000,96000];
    public LocalizedOption[] LayoutNames {get;}=[new("T787C02ABE0"), new("TF04CB441BB"), new("T3D6D92FF9B"), new("TDBB3C16A87"), new("T08D4421B83")];
    public int LayoutIndex {get;set;}
    public int TemplateCount {get;set;}=8;
    public double TemplateYaw {get;set;}
    public double TemplateElevation {get;set;}
    public double TemplateCoverage {get;set;}=90;
    public double TemplateEnergy {get;set;}=0;
    public string TemplateGroup {get;set;}=SoundstageIR.Core.TextCatalog.T("T20B85E0505");
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
            if(P.Sources.Sum(s=>s.Multiplicity)>30)throw new InvalidOperationException(SoundstageIR.Core.TextCatalog.T("T3E314792C4"));
            var s=new ReflectionPair{Name=SoundstageIR.Core.TextCatalog.T("T918B2B558A"),Group=SoundstageIR.Core.TextCatalog.T("T4EAFA9E925")};P.Sources.Add(s);Commit();RefreshSources();Selected=s;
        }),()=>Ready);
        DuplicateCommand=new ActionCommand(_=>Safe(Duplicate),()=>Ready&&Selected!=null);
        DeleteCommand=new ActionCommand(_=>{foreach(var s in Targets())P.Sources.Remove(s);Commit();RefreshSources();},()=>Ready&&Selected!=null);
        CopyCommand=new ActionCommand(_=>{clipboard=ProjectIO.Clone(Selected!);SetStatus(SoundstageIR.Core.TextCatalog.T("T3F6661C298"));},()=>Selected!=null);
        PasteCommand=new ActionCommand(_=>Paste(true),()=>Ready&&clipboard!=null&&Selected!=null);
        BatchCommand=new ActionCommand(_=>{clipboard=ProjectIO.Clone(Selected!);Paste(false);},()=>Ready&&Selected!=null&&Selection.Count>1);
        SeedCommand=new ActionCommand(_=>{P.Seed=Random.Shared.Next(1,int.MaxValue);Commit();},()=>Ready);
    }
    public async Task ApplyTemplateAsync(TemplateSelection selection)
    {
        if(Busy)return;
        if(selection.Speaker is {} speaker)SetSpeakerProject(speaker);else ApplyTemplateProject(selection.Project);SetStatus(SoundstageIR.Core.TextCatalog.T("T73D20904DB")+P.Name);PresetApplied?.Invoke();
        if(selection.Generate)await GenerateAsync();
    }
    internal static string FindRoot()
    {
        if(App.TestRoot!=null)return App.TestRoot;
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.txt")))return AppContext.BaseDirectory;
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir!=null){if(Directory.Exists(Path.Combine(dir.FullName,"Core"))&&Directory.Exists(Path.Combine(dir.FullName,"App")))return dir.FullName;dir=dir.Parent;}
        return AppContext.BaseDirectory;
    }
    public void ApplyReflectionEdit(ReflectionEditSession edit)
    {
        if(Busy)throw new InvalidOperationException(SoundstageIR.Core.TextCatalog.T("TD2033E3B6C"));
        var next=edit.Apply(TemplateProject);next.TemplateSources=ProjectIO.Clone(next.Sources);var selectedIds=Selection.Select(s=>s.Id).ToHashSet();var selectedId=Selected?.Id;
        P=next;Commit();RefreshSources();Selection=Sources.Where(s=>selectedIds.Contains(s.Id)).ToList();
        Selected=Sources.FirstOrDefault(s=>s.Id==selectedId)??Sources.FirstOrDefault();
        SetStatus(SoundstageIR.Core.TextCatalog.T("T1E53F168AE")+(edit.ClampedValues>0?SoundstageIR.Core.TextCatalog.T("T11CF0D9274"):"")+(edit.CurveSimplified?SoundstageIR.Core.TextCatalog.T("T90054D5305"):""));
    }
    public void SetStatus(string s){Status=s;Notify(nameof(Status));}
    public void Commit()
    {
        if(Localizing)return;
        int clamped=EditingLimits.Normalize(P);if(clamped>0)SetStatus(SoundstageIR.Core.TextCatalog.T("TF16C991F99"));
        string next=SerializeState();if(next==snapshot)return;undo.Push(snapshot);redo.Clear();snapshot=next;
        RefreshResultIdentity();
        if(!string.IsNullOrEmpty(LastExport))Status=stale?SoundstageIR.Core.TextCatalog.T("T97412F29A5"):SoundstageIR.Core.TextCatalog.T("TF51FCBFD23");
        LastExport="";
        Notify("");VisualChanged?.Invoke();CommandManager.InvalidateRequerySuggested();
    }
    void RefreshResultIdentity()
    {
        if(Speaker!=null){RefreshSpeakerResultIdentity();return;}
        if(Result==null){Stale=true;return;}
        var compare=ProjectIO.Clone(P);compare.Name=Result.Project.Name;compare.TemplateName=Result.Project.TemplateName;
        Stale=ProjectIO.Serialize(compare)!=ProjectIO.Serialize(Result.Project);
        if(!stale){Result.Project.Name=P.Name;Result.Project.TemplateName=P.TemplateName;}
    }
    public void SetProject(Project p)
    {undo.Push(snapshot);redo.Clear();P=p;if(Speaker!=null)Speaker.Field=P;SpeakerResult=null;Result=null;LastExport="";pendingEdits=false;AnalysisCache.Clear();ProjectFile="";snapshot=SerializeState();Stale=true;RefreshSources();Notify("");VisualChanged?.Invoke();}
    public void Undo(){if(CommitTemplateEdits?.Invoke()==false||undo.Count==0)return;redo.Push(snapshot);Restore(undo.Pop());}
    public void Redo(){if(CommitTemplateEdits?.Invoke()==false||redo.Count==0)return;undo.Push(snapshot);Restore(redo.Pop());}
    void Restore(string s){snapshot=s;RestoreState(s);LastExport="";RefreshResultIdentity();RefreshSources();Notify("");VisualChanged?.Invoke();}
    void RefreshSources()
    {
        Guid? id=Selected?.Id;Sources.Clear();foreach(var s in P.Sources)Sources.Add(s);Selection=[];
        Selected=Sources.FirstOrDefault(s=>s.Id==id)??Sources.FirstOrDefault();
    }
    List<ReflectionPair> Targets()=>Selection.Count>0?Selection.ToList():Selected==null?[]:[Selected];
    void Duplicate()
    {
        var add=Targets().Select(s=>s.CloneIndependent()).ToList();if(P.Sources.Sum(s=>s.Multiplicity)+add.Sum(s=>s.Multiplicity)>32)throw new InvalidOperationException(SoundstageIR.Core.TextCatalog.T("T3E314792C4"));
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
        if(TemplateCount<2||TemplateCount>32||TemplateCount%2!=0)throw new InvalidOperationException(SoundstageIR.Core.TextCatalog.T("TE704B44CD7"));
        var e=P.TemplateSources.FirstOrDefault()?.Left.Clone()??new Excitation();e.GainDb=TemplateEnergy;
        var generated=Templates.Create((Distribution)LayoutIndex,TemplateCount,TemplateYaw,TemplateElevation,TemplateCoverage,e,TemplateGroup);
        if(!TemplateReplace&&P.TemplateSources.Sum(s=>s.Multiplicity)+TemplateCount>32)throw new InvalidOperationException(SoundstageIR.Core.TextCatalog.T("T034F3727B0"));
        if(TemplateReplace)P.TemplateSources.Clear();P.TemplateSources.AddRange(generated);P.Sources=ProjectIO.Clone(P.TemplateSources);Commit();RefreshSources();
    }
    public async Task<bool> GenerateAsync()
    {
        if(Busy)return false;
        try
        {
            if(CommitTemplateEdits?.Invoke()==false)return false;
            Commit();ValidateState();speakerFailure=null;StopSpeakerPrecheck();var input=ProjectIO.Clone(P);var version=snapshot;Busy=true;Generating=true;Progress=0;Notify("");CommandManager.InvalidateRequerySuggested();
            cancellation=new();var progress=new Progress<(double Fraction,string Message)>(v=>{Progress=v.Fraction*100;Status=v.Message;Notify(nameof(Progress));Notify(nameof(Status));});
            if(Speaker!=null){var speakerInput=ProjectIO.Clone(Speaker);SpeakerResult=await Task.Run(()=>SoundstageIR.Core.Speakers.SpeakerGenerator.Generate(speakerInput,progress,cancellation.Token));Result=SpeakerResult.ForAudio();}
            else Result=await Task.Run(()=>Generator.Generate(input,progress,cancellation.Token));Stale=snapshot!=version;Status=SpeakerResultRisk?TextCatalog.T("Speaker.ReadyWithRisk"):SoundstageIR.Core.TextCatalog.T("T2C954ED050");return true;
        }
        catch(OperationCanceledException){Status=SoundstageIR.Core.TextCatalog.T("T2AFC7DC5EF");return false;}
        catch(Exception e){Status=SoundstageIR.Core.TextCatalog.T("TCFDCD16119")+e.Message;if(SpatialSpeaker)speakerFailure=Status;return false;}
        finally{Generating=false;Busy=false;cancellation?.Dispose();cancellation=null;Notify("");VisualChanged?.Invoke();CommandManager.InvalidateRequerySuggested();}
    }
    public void Cancel()=>cancellation?.Cancel();
    public async Task<string?> ExportAsync()
    {
        if(Busy||CommitTemplateEdits?.Invoke()==false)return null;Commit();if(Result==null||Stale){SetStatus(SoundstageIR.Core.TextCatalog.T("TB21EEC1263"));return null;}
        try{Busy=true;Notify("");var r=Result;string path=ExportParent;LastExport=await Task.Run(()=>SpeakerResult is {} speaker?SoundstageIR.Core.Speakers.SpeakerExporter.Export(speaker,path):Exporter.Export(r,path));Status=SoundstageIR.Core.TextCatalog.T("T961EF8B1BC")+LastExport;return LastExport;}
        catch(Exception e){Status=SoundstageIR.Core.TextCatalog.T("TC55E9D64E5")+e.Message;return null;}
        finally{Busy=false;Notify("");CommandManager.InvalidateRequerySuggested();}
    }
    void SaveDialog()
    {
        if(CommitTemplateEdits?.Invoke()==false)return;
        Commit();ValidateState();Directory.CreateDirectory(Path.Combine(Root,"projects"));
        var dialog=new SaveFileDialog{Title=SoundstageIR.Core.TextCatalog.T("T429EA3AF44"),Filter=SoundstageIR.Core.TextCatalog.T("T2678356BA9"),InitialDirectory=Path.Combine(Root,"projects"),FileName=SuggestedProjectFileName};
        if(dialog.ShowDialog()==true){SaveProjectFile(dialog.FileName);ProjectFile=dialog.FileName;SetStatus(SoundstageIR.Core.TextCatalog.T("T0EAF9C8B45")+ProjectFile);}
    }
    void OpenDialog(){var d=new OpenFileDialog{Title=SoundstageIR.Core.TextCatalog.T("TB48B651829"),Filter=SoundstageIR.Core.TextCatalog.T("T2678356BA9"),InitialDirectory=Path.Combine(Root,"projects")};if(d.ShowDialog()==true){LoadProjectFile(d.FileName);ProjectFile=d.FileName;SetStatus(SoundstageIR.Core.TextCatalog.T("TD26B962E05")+ProjectFile);PresetApplied?.Invoke();}}
    void LoadCards()
    {
        PresetCards.Clear();
        foreach(var p in Presets.All)PresetCards.Add(new(TextCatalog.Source(p.Name),
            SimpleSpeaker&&p.Name=="自由场"?TextCatalog.T("Speaker.SimpleFree"):SimpleSpeaker&&p==Presets.Blank?TextCatalog.T("Speaker.SimpleBlank"):TextCatalog.Source(p.Description),
            SimpleSpeaker?TextCatalog.F("Speaker.SimpleDetails",p.Rt,p.WetPercent):TextCatalog.Diagnostic(p.Details),p));
    }
    void Safe(Action action){try{action();}catch(Exception e){SetStatus(e.Message);}}
}
