using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using System;
using System.Windows;

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

                var window = new Wpf.MainWindow(aim)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                window.ShowDialog();

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
