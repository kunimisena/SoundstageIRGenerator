using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SoundstageIR.Core;

/// <summary>Presentation resources only. Does not change numeric culture or project data.</summary>
public static class TextCatalog
{
    public sealed record Entry(string Zh,string En);
    static readonly Dictionary<string,Entry> entries=Read();
    static readonly Dictionary<string,string> sourceKeys=entries.ToDictionary(x=>x.Value.Zh,x=>x.Key);
    static readonly Dictionary<string,string> englishKeys=entries.GroupBy(x=>x.Value.En).ToDictionary(g=>g.Key,g=>g.First().Key);
    static readonly List<(Entry Entry,Regex Zh,Regex En)> patterns=entries.Values.Where(e=>Regex.IsMatch(e.Zh,@"\{\d+")).OrderByDescending(e=>e.Zh.Length).Select(e=>(e,Pattern(e.Zh),Pattern(e.En))).ToList();
    static readonly List<Entry> fragments=entries.Values.Where(e=>!e.Zh.Contains('{')&&!e.Zh.Contains('\n')&&e.Zh.Length>1).OrderByDescending(e=>e.Zh.Length).ToList();
    static string language="zh-CN";
    public static string Language=>language;
    public static bool English=>language=="en";
    public static IReadOnlyDictionary<string,Entry> Entries=>entries;
    public static void SetLanguage(string value)
    {
        string next=value.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh-CN":"en";
        language=next;
    }
    public static string ForSystem(CultureInfo culture)=>culture.TwoLetterISOLanguageName=="zh"?"zh-CN":"en";
    public static string T(string key)=>entries.TryGetValue(key,out var e)?(English?e.En:e.Zh):throw new KeyNotFoundException("Missing localization key: "+key);
    public static string F(string key,params object?[] args)=>string.Format(CultureInfo.InvariantCulture,T(key),args);
    public static string Identity(string text)=>sourceKeys.TryGetValue(text,out var id)||englishKeys.TryGetValue(text,out id)?id:text;
    public static string Source(string text)=>sourceKeys.TryGetValue(text,out var key)?T(key):text;

    /// <summary>Localizes trusted diagnostic text, not user names or file paths. Arguments stay opaque.</summary>
    public static string Diagnostic(string text)
    {
        if(string.IsNullOrEmpty(text))return text;
        if(sourceKeys.TryGetValue(text,out var id)||englishKeys.TryGetValue(text,out id))return T(id);
        foreach(var p in patterns)
        {
            var m=p.Zh.Match(text);if(!m.Success)m=p.En.Match(text);if(!m.Success)continue;
            string target=English?p.Entry.En:p.Entry.Zh;
            return Regex.Replace(target,@"\{(?<n>\d+)(?:[^{}]*)\}",v=>{string a=m.Groups["a"+v.Groups["n"].Value].Value;return v.Groups["n"].Value=="0"&&(p.Entry.Zh.StartsWith("{0}：")||p.Entry.Zh.StartsWith("{0}须")||p.Entry.Zh.StartsWith("{0}需要")||p.Entry.Zh.StartsWith("{0}控制点"))?Diagnostic(a):a;});
        }
        if(text.Contains('\n'))return string.Join("\n",text.Split('\n').Select(Diagnostic));
        foreach(string suffix in new[]{" 横坐标"," 纵坐标"})
        {
            var entry=entries[sourceKeys[suffix]];
            foreach(string label in new[]{entry.Zh,entry.En})
                if(text.EndsWith(label,StringComparison.Ordinal))return Diagnostic(text[..^label.Length])+(English?entry.En:entry.Zh);
        }

        // Prefixes are standalone resource fragments such as “Export failed: ”.
        foreach(var e in fragments)
        {
            foreach(var source in new[]{e.Zh,e.En})
                if(text.StartsWith(source,StringComparison.Ordinal)&&(source.EndsWith(' ')||source.EndsWith('：')||source.EndsWith(':')||text[source.Length..].StartsWith(" · ")))
                    return (English?e.En:e.Zh)+((text[source.Length..].StartsWith(" · ")||source.EndsWith('：')||source.EndsWith(':'))?Diagnostic(text[source.Length..]):text[source.Length..]);
        }
        return text;
    }
    static Regex Pattern(string value)
    {
        var parts=Regex.Split(value,@"(\{\d+(?:[^{}]*)\})");var used=new HashSet<string>();
        string body=string.Concat(parts.Select(p=>{var m=Regex.Match(p,@"^\{(\d+)");if(!m.Success)return Regex.Escape(p);string n="a"+m.Groups[1].Value;return used.Add(n)?"(?<"+n+">.*?)":"\\k<"+n+">";}));
        return new Regex("\\A"+body+"\\z",RegexOptions.CultureInvariant|RegexOptions.Singleline,TimeSpan.FromMilliseconds(100));
    }
    static Dictionary<string,Entry> Read()
    {
        using var stream=typeof(TextCatalog).Assembly.GetManifestResourceStream("SoundstageIR.Core.Strings.json")!;
        return JsonSerializer.Deserialize<Dictionary<string,Entry>>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
}
