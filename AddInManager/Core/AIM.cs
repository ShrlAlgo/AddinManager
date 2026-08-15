using System;
using System.Windows;

using AddInManager.DebugTools;
using AddInManager.Localization;
using AddInManager.Models;
using AddInManager.Persistence;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AddInManager.Core
{
    public sealed class AIM
    {
        public Result ExecuteCommand(ExternalCommandData data, ref string message, ElementSet elements, bool faceless)
        {
            if (ActiveCmd != null && ActiveCmdItem != null && faceless)
            {
                return RunActiveCommand(data, ref message, elements);
            }

            bool dialogResult;
            do
            {
                LanguageManager.RestartRequested = false;
                LanguageManager.ApplySavedLanguage();
                var mainWindow = new Wpf.MainWindow(this);
                dialogResult = mainWindow.ShowDialog() == true;
            } while (LanguageManager.RestartRequested);

            if (!dialogResult)
            {
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
            var assemLoader = new AssemLoader();
            Result result = Result.Failed;
            try
            {
                if (ActiveCmd == null || ActiveCmdItem == null)
                {
                    message = "No active command is selected.";
                    DebugLogger.Instance.Error(message);
                    return result;
                }

                var filePath = ActiveCmd.FilePath;
                DebugLogger.Instance.Info($"Run command: {ActiveCmdItem.FullClassName} ({filePath})");
                assemLoader.HookAssemblyResolve();
                var assembly = assemLoader.LoadAddinsToTempFolder(filePath, false);
                if (null == assembly)
                {
                    DebugLogger.Instance.Error($"Assembly load failed: {filePath}");
                    result = Result.Failed;
                }
                else
                {
                    ActiveTempFolder = assemLoader.TempFolder;
                    var externalCommand = assembly.CreateInstance(ActiveCmdItem.FullClassName) as IExternalCommand;
                    if (externalCommand == null)
                    {
                        DebugLogger.Instance.Error($"Can not create command instance: {ActiveCmdItem.FullClassName}");
                        result = Result.Failed;
                    }
                    else
                    {
                        ActiveEC = externalCommand;
                        result = ActiveEC.Execute(data, ref message, elements);
                        DebugLogger.Instance.Info($"Command execution completed, result: {result}");
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
                try
                {
                    assemLoader.UnhookAssemblyResolve();
                }
                catch (Exception cleanupException)
                {
                    LogCleanupException(cleanupException, "UnhookAssemblyResolve");
                }

                try
                {
                    assemLoader.CopyGeneratedFilesBack();
                }
                catch (Exception cleanupException)
                {
                    LogCleanupException(cleanupException, "CopyGeneratedFilesBack");
                }
            }
            return result;
        }

        private static void LogCleanupException(Exception exception, string context)
        {
            try
            {
                DebugLogger.Instance.Error(exception, context);
            }
            catch (Exception loggingException)
            {
                System.Diagnostics.Debug.WriteLine($"{context}: {exception}{Environment.NewLine}Logging failed: {loggingException}");
            }
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
