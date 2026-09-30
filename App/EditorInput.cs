using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace StatisticalFieldStudio;
internal static class EditorInput
{
    public static void Update(TextBox box)
    {
        var binding=box.GetBindingExpression(TextBox.TextProperty);if(binding?.IsDirty==true)binding.UpdateSource();
    }
    public static bool HasDirty(DependencyObject item)
    {
        if(Validation.GetHasError(item))return true;
        if(item is TextBox box&&box.GetBindingExpression(TextBox.TextProperty)?.IsDirty==true)return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)if(HasDirty(VisualTreeHelper.GetChild(item,i)))return true;
        return false;
    }
    public static void Commit(DependencyObject item)
    {
        if(item is TextBox box)EditorInput.Update(box);
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)Commit(VisualTreeHelper.GetChild(item,i));
    }
    public static bool HasErrors(DependencyObject item)
    {
        if(Validation.GetHasError(item))return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)if(HasErrors(VisualTreeHelper.GetChild(item,i)))return true;
        return false;
    }
}
