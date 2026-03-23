using System;
using System.Windows;

using AddInManager.DebugTools;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AddInManager
{
    public sealed class AIM
    {
        public Result ExecuteCommand(ExternalCommandData data, ref string message, ElementSet elements, bool faceless)
        {
            if (ActiveCmd != null && faceless)
            {
                return RunActiveCommand(data, ref message, elements);
            }

            DebugLogger.Instance.Info("打开主窗口");
            var mainWindow = new Wpf.MainWindow(this);
            var dialogResult = mainWindow.ShowDialog();

            if (dialogResult != true)
            {
                DebugLogger.Instance.Info("用户取消，主窗口已关闭");
                return Result.Cancelled;
            }

            // 窗口关闭后，检查是否有选中的命令需要执行
            if (ActiveCmd != null && ActiveCmdItem != null)
            {
                return RunActiveCommand(data, ref message, elements);
            }
            
            return Result.Succeeded; // 如果没有命令要执行，返回成功
        }

        public string ActiveTempFolder { get; set; } = string.Empty;

        private Result RunActiveCommand(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var filePath = ActiveCmd.FilePath;
            DebugLogger.Instance.Info($"执行命令: {ActiveCmdItem?.FullClassName} ({filePath})");
            var assemLoader = new AssemLoader();
            Result result;
            try
            {
                assemLoader.HookAssemblyResolve();
                var assembly = assemLoader.LoadAddinsToTempFolder(filePath, false);
                if (null == assembly)
                {
                    DebugLogger.Instance.Error($"程序集加载失败: {filePath}");
                    result = Result.Failed;
                }
                else
                {
                    ActiveTempFolder = assemLoader.TempFolder;
                    var externalCommand = assembly.CreateInstance(ActiveCmdItem.FullClassName) as IExternalCommand;
                    if (externalCommand == null)
                    {
                        DebugLogger.Instance.Error($"无法创建命令实例: {ActiveCmdItem.FullClassName}");
                        result = Result.Failed;
                    }
                    else
                    {
                        ActiveEC = externalCommand;
                        result = ActiveEC.Execute(data, ref message, elements);
                        DebugLogger.Instance.Info($"命令执行完成，结果: {result}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.Error(ex, "RunActiveCommand");
                MessageBox.Show(ex.ToString());
                result = Result.Failed;
            }
            finally
            {
                assemLoader.UnhookAssemblyResolve();
                assemLoader.CopyGeneratedFilesBack();
            }
            return result;
        }

        public static AIM Instance
        {
            get
            {
                if (m_inst == null)
                {
                    lock (typeof(AIM))
                    {
                        m_inst ??= new AIM();
                    }
                }
                return m_inst;
            }
        }

        private AIM()
        {
            AddinManager = new AddinManager();
            ActiveCmd = null;
            ActiveCmdItem = null;
            ActiveApp = null;
            ActiveAppItem = null;
        }

        public IExternalCommand ActiveEC { get; set; }

        public Addin ActiveCmd { get; set; }

        public AddinItem ActiveCmdItem { get; set; }

        public Addin ActiveApp { get; set; }

        public AddinItem ActiveAppItem { get; set; }

        public AddinManager AddinManager { get; set; }

        private static volatile AIM m_inst;
    }
}
