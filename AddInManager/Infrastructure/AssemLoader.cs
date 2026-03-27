using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

using AddInManager.DebugTools;

using DiagDebug = System.Diagnostics.Debug;

namespace AddInManager
{
    public class AssemLoader
    {
        public string OriginalFolder { get; set; }
        public string TempFolder { get; set; }

        private List<string> m_refedFolders;
        private Dictionary<string, DateTime> m_copiedFiles;
        private bool m_parsingOnly;

        public static string m_resolvedAssemPath = string.Empty;

        public AssemLoader()
        {
            TempFolder = string.Empty;
            m_refedFolders = new List<string>();
            m_copiedFiles = new Dictionary<string, DateTime>();
        }

        // ... CopyGeneratedFilesBack 保持不变 ...
        public void CopyGeneratedFilesBack()
        {
            if (string.IsNullOrEmpty(TempFolder) || !Directory.Exists(TempFolder)) return;

            var files = Directory.GetFiles(TempFolder, "*.*", SearchOption.AllDirectories);
            foreach (var text in files)
            {
                if (m_copiedFiles.ContainsKey(text))
                {
                    var dateTime = m_copiedFiles[text];
                    var fileInfo = new FileInfo(text);
                    if (fileInfo.LastWriteTime > dateTime)
                    {
                        var text2 = text.Remove(0, TempFolder.Length);
                        var text3 = OriginalFolder + text2;
                        FileUtils.CopyFile(text, text3);
                    }
                }
                // 新生成的文件不建议盲目拷回，可能会污染源目录，视需求而定
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
                DebugLogger.Instance.Warning($"AssemLoader: Invalid file path: {originalFilePath}");
                return null;
            }
            DebugLogger.Instance.Info($"AssemLoader: Loading {System.IO.Path.GetFileName(originalFilePath)} (Parsing only: {parsingOnly})");
            m_parsingOnly = parsingOnly;
            OriginalFolder = Path.GetDirectoryName(originalFilePath);

            var stringBuilder = new StringBuilder(Path.GetFileNameWithoutExtension(originalFilePath));
            stringBuilder.Append(parsingOnly ? "-Parsing-" : "-Executing-");

            TempFolder = FileUtils.CreateTempFolder(stringBuilder.ToString());

            // 【建议】在此处，除了复制主DLL，最好把同目录下的所有 .dll 都复制过去
            // 这样可以避免 AssemblyResolve 频繁触发，解决大部分 NuGet 依赖问题
            // CopyAllDllsToTemp(OriginalFolder, TempFolder); // 这是一个建议实现的辅助方法

            var assembly = CopyAndLoadAddin(originalFilePath, parsingOnly);
            if (null == assembly || !IsAPIReferenced(assembly))
            {
                DebugLogger.Instance.Warning($"AssemLoader: Assembly does not reference Revit API or failed to load: {System.IO.Path.GetFileName(originalFilePath)}");
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
                // 假设 FileUtils.CopyFileToFolder 会处理文件复制
                // 关键点：如果你的 FileUtils 不支持复制子文件夹（如 zh-CN），资源加载依然会失败
                destPath = FileUtils.CopyFileToFolder(srcFilePath, TempFolder, onlyCopyRelated, list);

                if (string.IsNullOrEmpty(destPath))
                {
                    return null;
                }
                foreach (var fileInfo in list)
                {
                    if (!m_copiedFiles.ContainsKey(fileInfo.FullName))
                        m_copiedFiles.Add(fileInfo.FullName, fileInfo.LastWriteTime);
                }
            }
            else
            {
                // 如果文件已存在，计算目标路径
                string fileName = Path.GetFileName(srcFilePath);
                destPath = Path.Combine(TempFolder, fileName);
            }

            return LoadAddin(destPath);
        }

        private Assembly LoadAddin(string filePath)
        {
            Assembly assembly = null;
            try
            {
                Monitor.Enter(this);
                // 【关键修改 1】使用 LoadFrom 而不是 LoadFile
                // LoadFrom 会自动在 filePath 所在的目录中查找依赖项，这解决了大部分 NuGet 包加载失败的问题
                // LoadFile 这是一个纯粹的文件加载，不带上下文，不会去旁边找依赖
                assembly = Assembly.LoadFrom(filePath);
                DebugLogger.Instance.Info($"AssemLoader: Successfully loaded assembly: {System.IO.Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                // 增加简单的错误输出，方便调试
                DiagDebug.WriteLine($"LoadAddin Failed: {filePath}, Error: {ex.Message}");
                DebugLogger.Instance.Error(ex, $"AssemLoader.LoadAddin: {System.IO.Path.GetFileName(filePath)}");
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

                // 【关键修改 2】绝对不要在 AssemblyResolve 中手动处理 .resources
                // 那个 "长度不能小于0" 的错误就是因为这里返回了错误的东西或者试图去加载主程序集
                // 如果请求的是 .resources，直接返回 null，让 CLR 自己去临时目录的子文件夹里找
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

                // 4. 如果还没找到，处理一些特殊情况（比如 XMLSerializers）
                // 这里的逻辑可以保留，但通常用处不大
                if (simpleName.EndsWith(".XmlSerializers", StringComparison.OrdinalIgnoreCase))
                {
                    // 忽略序列化程序集请求
                    return null;
                }

                // 5. 【可选】最后尝试手动弹窗选择（原代码逻辑），
                // 但通常对于依赖项来说，弹窗很烦人，建议只针对主程序集弹窗，或者直接返回null
                // 如果这是为了解决找不到依赖的问题，上面 LoadFrom 改好后这里应该很少进来了
                // 只有当你确实需要弹窗时保留下面代码

                var assemblySelector = new Wpf.AssemblySelectorWindow(args.Name);
                if (assemblySelector.ShowDialog() == true)
                {
                    text = assemblySelector.ResultPath;
                    assembly = CopyAndLoadAddin(text, true);
                }

            }
            return assembly;
        }

        private string SearchAssemblyFileInTempFolder(string simpleName)
        {
            var extensions = new string[] { ".dll", ".exe" };
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(TempFolder, simpleName + ext);
                if (File.Exists(candidate)) return candidate;
            }
            return string.Empty;
        }

        private string SearchAssemblyFileInOriginalFolders(string simpleName)
        {
            var extensions = new string[] { ".dll", ".exe" };
            foreach (var folder in m_refedFolders)
            {
                foreach (var ext in extensions)
                {
                    var candidate = Path.Combine(folder, simpleName + ext);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return string.Empty;
        }

        private bool IsAPIReferenced(Assembly assembly)
        {
            foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
            {
                if (referencedAssemblyName.Name != null && referencedAssemblyName.Name.IndexOf("RevitAPI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
