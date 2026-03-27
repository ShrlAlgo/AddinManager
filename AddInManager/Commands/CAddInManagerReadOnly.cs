using System;
using System.Windows;

using AddInManager.Core;
using AddInManager.Localization;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace AddInManager
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CAddInManagerReadOnly : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                var aim = AIM.Instance;
                do
                {
                    LanguageManager.RestartRequested = false;
                    var window = new Wpf.MainWindow(aim)
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    };
                    window.ShowDialog();
                } while (LanguageManager.RestartRequested);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
