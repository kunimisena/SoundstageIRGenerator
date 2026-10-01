using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;

public static class UiLanguage
{
    sealed record Preferences(string Language);
    static string settingsFile="";
    public static void Initialize(string root)
    {
        settingsFile=Path.Combine(root,"settings","language.json");
        string selected=TextCatalog.ForSystem(CultureInfo.CurrentUICulture);
        try {if(File.Exists(settingsFile))selected=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsFile))?.Language??selected;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { /* A malformed preference falls back to the system language. */ }
        TextCatalog.SetLanguage(selected);ApplyResources();
    }
    public static string? Select(string language)
    {
        TextCatalog.SetLanguage(language);ApplyResources();
        try {Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);File.WriteAllText(settingsFile,JsonSerializer.Serialize(new Preferences(TextCatalog.Language)));return null;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {return TextCatalog.F("LanguagePreferenceError",e.Message);}
    }
    public static void ApplyResources()
    {
        if(Application.Current==null)return;
        foreach(var (key,_) in TextCatalog.Entries)Application.Current.Resources[key]=TextCatalog.T(key);
    }
}
