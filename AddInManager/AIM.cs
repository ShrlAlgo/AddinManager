using System;
using System.Windows;

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
            
            var mainWindow = new Wpf.MainWindow(this);
            var dialogResult = mainWindow.ShowDialog();

            if (dialogResult != true)
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
            // 防御性检查：确保 ActiveCmd 不为空
            if (this.ActiveCmd == null)
            {
                MessageBox.Show("错误：ActiveCmd 为 null");
                return Result.Failed;
            }

            // 防御性检查：确保 ActiveCmdItem 不为空
            if (this.ActiveCmdItem == null)
            {
                MessageBox.Show("错误：ActiveCmdItem 为 null");
                return Result.Failed;
            }

            var filePath = ActiveCmd.FilePath;

            // 检查文件是否存在
            if (!System.IO.File.Exists(filePath))
            {
                MessageBox.Show($"错误：找不到文件 {filePath}");
                return Result.Failed;
            }

            var assemLoader = new AssemLoader();
            Result result = Result.Failed; // 默认失败

            try
            {
                assemLoader.HookAssemblyResolve();

                var assembly = assemLoader.LoadAddinsToTempFolder(filePath, false);

                if (assembly == null)
                {
                    message = "Assembly 加载失败，返回了 null";
                    result = Result.Failed;
                }
                else
                {
                    ActiveTempFolder = assemLoader.TempFolder;

                    string className = ActiveCmdItem.FullClassName;
                    if (string.IsNullOrEmpty(className)) throw new Exception("类名为空");

                    var instanceObj = assembly.CreateInstance(className);

                    if (instanceObj == null)
                    {
                        message = $"无法创建实例: {className}。请检查类名是否正确或是否有无参构造函数。";
                        result = Result.Failed;
                    }
                    else
                    {
                        if (instanceObj is not IExternalCommand externalCommand)
                        {
                            message = $"{className} 没有实现 IExternalCommand 接口";
                            result = Result.Failed;
                        }
                        else
                        {
                            ActiveEC = externalCommand;
                            result = ActiveEC.Execute(data, ref message, elements);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"执行异常:\n{ex.Message}\n\n堆栈:\n{ex.StackTrace}");
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
