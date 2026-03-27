using AddInManager.Properties;
using System.Windows;

namespace AddInManager.Wpf.Dialogs
{
    public static class FailedToRunECDialog
    {
        public static MessageBoxResult Show(string ecName)
        {
            var text = string.Format(Resources.FailedToRunEC, ecName);
            return MessageBox.Show(text, Resources.AppName, MessageBoxButton.OK);
        }
    }
}
