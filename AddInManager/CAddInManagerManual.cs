using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using System;
using System.Windows;

namespace AddInManager
{
    [Transaction(TransactionMode.Manual)]
    public class CAddInManagerManual : IExternalCommand
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

                var dialogResult = window.ShowDialog();

                // 检查是否需要执行命令
                if (dialogResult == true && aim.ActiveCmd != null && aim.ActiveCmdItem != null)
                {
                    // 调用AIM的ExecuteCommand方法来执行选中的命令
                    return aim.ExecuteCommand(commandData, ref message, elements, true);
                }

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
