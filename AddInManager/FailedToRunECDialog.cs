using System.Windows;

using AddInManager.Properties;

namespace AddInManager
{
    public class FailedToRunECDialog
    {
        public static MessageBoxResult Show(string ecName)
        {
            var text = string.Format(Resources.FailedToRunEC, ecName);
            return MessageBox.Show(text, Resources.AppName, MessageBoxButton.OK);
        }
    }
}
