using System;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AddInManager.DebugTools
{
    /// <summary>
    /// Revit external command that opens the Debug Log Viewer window.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CDebugLogViewer : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var window = new Wpf.LogViewerWindow
                {
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
                };
                window.Show();
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
