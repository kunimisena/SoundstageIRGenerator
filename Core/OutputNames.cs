using System.Text.RegularExpressions;
namespace StatisticalField.Core;
public static class OutputNames
{
    static readonly object Gate=new();
    public static string Safe(string? name)
    {
        string s=string.Concat((name??"").Select(c=>char.IsControl(c)||Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim().TrimEnd('.');
        if(string.IsNullOrWhiteSpace(s))s="未命名声场";
        if(Regex.IsMatch(s,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))s="_"+s;
        return s.Length>64?s[..64].TrimEnd('.',' '):s;
    }
    public static string Label(Project p)=>Safe(p.Name==p.TemplateName||string.IsNullOrWhiteSpace(p.TemplateName)?p.Name:p.TemplateName+"_"+p.Name);
    public static string File(Project p,string suffix)=>Label(p)+"_"+suffix;
    public static string NewDirectory(string parent,Project p)
    {
        lock(Gate){Directory.CreateDirectory(parent);string stem=Label(p)+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"),path=Path.Combine(parent,stem);int i=2;while(Directory.Exists(path))path=Path.Combine(parent,stem+"_"+(i++).ToString("00"));Directory.CreateDirectory(path);return path;}
    }
    public static string UniqueFile(string parent,string stem,string extension)
    {
        Directory.CreateDirectory(parent);string name=Safe(stem),path=Path.Combine(parent,name+extension);int i=2;while(System.IO.File.Exists(path))path=Path.Combine(parent,name+"_"+(i++).ToString("00")+extension);return path;
    }
}
