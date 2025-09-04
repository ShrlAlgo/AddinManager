using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using System;
using System.Windows;

namespace AddInManager
{
    public static class StaticUtil
    {
        public static void ShowWarning(Exception e)
        {
            ShowWarning(e.Message);
        }

        public static void ShowWarning(string msg)
        {
            MessageBox.Show(msg, "插件管理", MessageBoxButton.OK, MessageBoxImage.Exclamation);
        }

        public static string m_ecFullName = typeof(IExternalCommand).FullName;

        public static string m_eaFullName = typeof(IExternalApplication).FullName;

        public static RegenerationOption m_regenOption;

        public static TransactionMode m_tsactMode= TransactionMode.Manual;
    }
}
