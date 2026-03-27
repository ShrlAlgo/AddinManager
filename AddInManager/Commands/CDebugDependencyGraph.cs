using System;

using AddInManager.Core;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AddInManager.Commands
{
    /// <summary>
    /// Revit external command that opens the Dependency Graph window.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CDebugDependencyGraph : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var window = new Wpf.DependencyGraphWindow(AIM.Instance)
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
