using System;
using System.Windows;

using AddInManager.Core;
using AddInManager.Localization;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

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
                var previousActiveCmd = aim.ActiveCmd;
                var previousActiveCmdItem = aim.ActiveCmdItem;
                bool dialogResult;
                do
                {
                    LanguageManager.RestartRequested = false;
                    LanguageManager.ApplySavedLanguage();
                    var window = new Wpf.MainWindow(aim)
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    };
                    dialogResult = window.ShowDialog() == true;
                } while (LanguageManager.RestartRequested);

                // 检查是否需要执行命令
                if (dialogResult && aim.ActiveCmd != null && aim.ActiveCmdItem != null)
                {
                    // 调用AIM的ExecuteCommand方法来执行选中的命令
                    return aim.ExecuteCommand(commandData, ref message, elements, true);
                }

                // 未执行新命令时保留上一次执行记录，供无界面模式继续使用。
                aim.ActiveCmd = previousActiveCmd;
                aim.ActiveCmdItem = previousActiveCmdItem;
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
