using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace AddInManager
{
    public class AssemLoader
    {
        public string OriginalFolder { get; set; }
        public string TempFolder { get; set; }

        private List<string> m_refedFolders;
        private Dictionary<string, DateTime> m_copiedFiles;
        private bool m_parsingOnly;

        private static string m_dotnetDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();

        public static string m_resolvedAssemPath = string.Empty;
        private string m_revitAPIAssemblyFullName;

        public AssemLoader()
        {
            TempFolder = string.Empty;
            m_refedFolders = new List<string>();
            m_copiedFiles = new Dictionary<string, DateTime>();
        }

        public void CopyGeneratedFilesBack()
        {
            var files = Directory.GetFiles(TempFolder, "*.*", SearchOption.AllDirectories);
            foreach(var text in files)
            {
                if(m_copiedFiles.ContainsKey(text))
                {
                    var dateTime = m_copiedFiles[text];
                    var fileInfo = new FileInfo(text);
                    if(fileInfo.LastWriteTime > dateTime)
                    {
                        var text2 = text.Remove(0, TempFolder.Length);
                        var text3 = OriginalFolder + text2;
                        FileUtils.CopyFile(text, text3);
                    }
                } else
                {
                    var text4 = text.Remove(0, TempFolder.Length);
                    var text5 = OriginalFolder + text4;
                    FileUtils.CopyFile(text, text5);
                }
            }
        }

        public void HookAssemblyResolve()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        public void UnhookAssemblyResolve()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
        }

        public Assembly LoadAddinsToTempFolder(string originalFilePath, bool parsingOnly)
        {
            if (string.IsNullOrEmpty(originalFilePath) || originalFilePath.StartsWith("\\") || !File.Exists(originalFilePath))
            {
                return null;
            }
            m_parsingOnly = parsingOnly;
            OriginalFolder = Path.GetDirectoryName(originalFilePath);

            var stringBuilder = new StringBuilder(Path.GetFileNameWithoutExtension(originalFilePath));
            stringBuilder.Append(parsingOnly ? "-Parsing-" : "-Executing-");

            TempFolder = FileUtils.CreateTempFolder(stringBuilder.ToString());

            var assembly = CopyAndLoadAddin(originalFilePath, parsingOnly);
            if (null == assembly || !IsAPIReferenced(assembly))
            {
                return null;
            }
            return assembly;
        }

        private Assembly CopyAndLoadAddin(string srcFilePath, bool onlyCopyRelated)
        {
            var destPath = string.Empty;
            if (!FileUtils.FileExistsInFolder(srcFilePath, TempFolder))
            {
                var directoryName = Path.GetDirectoryName(srcFilePath);
                if (!m_refedFolders.Contains(directoryName))
                {
                    m_refedFolders.Add(directoryName);
                }
                var list = new List<FileInfo>();
                destPath = FileUtils.CopyFileToFolder(srcFilePath, TempFolder, onlyCopyRelated, list);
                if (string.IsNullOrEmpty(destPath))
                {
                    return null;
                }
                foreach (var fileInfo in list)
                {
                    m_copiedFiles.Add(fileInfo.FullName, fileInfo.LastWriteTime);
                }
            }
            return LoadAddin(destPath);
        }

        private Assembly LoadAddin(string filePath)
        {
            Assembly assembly = null;
            try
            {
                Monitor.Enter(this);
                assembly = Assembly.LoadFile(filePath);
            }
            catch (Exception ex)
            {
                // 增加简单的错误输出，方便调试
                Debug.WriteLine($"LoadAddin Failed: {filePath}, Error: {ex.Message}");
                throw;
            }
            finally
            {
                Monitor.Exit(this);
            }
            return assembly;
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            Assembly assembly = null;
            lock (this)
            {
                var assemblyNameObj = new AssemblyName(args.Name);
                var simpleName = assemblyNameObj.Name;

                if (simpleName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) ||
                    args.Name.Contains(".resources"))
                {
                    return null;
                }

                // 1. 先在临时目录找
                var text = SearchAssemblyFileInTempFolder(simpleName);
                if (File.Exists(text))
                {
                    return LoadAddin(text);
                }

                // 2. 临时目录没有，去源目录找
                text = SearchAssemblyFileInOriginalFolders(simpleName);

                // 3. 如果源目录找到了，复制到临时目录并加载
                if (!string.IsNullOrEmpty(text))
                {
                    assembly = CopyAndLoadAddin(text, true);
                    return assembly;
                }

                if (simpleName.EndsWith(".XmlSerializers", StringComparison.OrdinalIgnoreCase))
                {
                    // 忽略序列化程序集请求
                    return null;
                }

                // 5. 【可选】最后尝试手动弹窗选择（原代码逻辑），
                // 但通常对于依赖项来说，弹窗很烦人，建议只针对主程序集弹窗，或者直接返回null
                // 如果这是为了解决找不到依赖的问题，上面 LoadFrom 改好后这里应该很少进来了
                // 只有当你确实需要弹窗时保留下面代码
                /*
                var assemblySelector = new Wpf.AssemblySelectorWindow(args.Name);
                if (assemblySelector.ShowDialog() == true)
                {
                    text = assemblySelector.ResultPath;
                    assembly = CopyAndLoadAddin(text, true);
                }
                */
            }
            return assembly;
        }

        private string SearchAssemblyFileInTempFolder(string simpleName)
        {
            var extensions = new string[] { ".dll", ".exe" };
            foreach (var ext in extensions)
            {
                string path = Path.Combine(TempFolder, simpleName + ext);
                if (File.Exists(path)) return path;
            }
            return string.Empty;
        }

        private string SearchAssemblyFileInOriginalFolders(string simpleName)
        {
            var extensions = new string[] { ".dll", ".exe" };


            foreach (var ext in extensions)
            {
                string path = Path.Combine(m_dotnetDir, simpleName + ext);
                if (File.Exists(path)) return path;
            }

            // 2. 在所有引用过的源目录中找
            foreach (var ext in extensions)
            {
                foreach (var folder in m_refedFolders)
                {
                    string path = Path.Combine(folder, simpleName + ext);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            // 3. 原代码中关于 Regression 的逻辑（看起来是特定环境的，如果不需要建议删除）

            return null;
        }

        private bool IsAPIReferenced(Assembly assembly)
        {
            // 保持原逻辑不变
            if (string.IsNullOrEmpty(m_revitAPIAssemblyFullName))
            {
                foreach (var assembly2 in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Compare(assembly2.GetName().Name, "RevitAPI", true) == 0)
                    {
                        m_revitAPIAssemblyFullName = assembly2.GetName().Name;
                        break;
                    }
                }
            }
            // 防止未加载 RevitAPI 时崩溃
            if (string.IsNullOrEmpty(m_revitAPIAssemblyFullName)) return true;

            foreach (var assemblyName in assembly.GetReferencedAssemblies())
            {
                if (m_revitAPIAssemblyFullName == assemblyName.Name)
                {
                    return true;
                }
            }
            return false;
        }
    }
}