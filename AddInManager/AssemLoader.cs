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

        // 获取 .NET 运行时目录
        private static string m_dotnetDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();

        private string m_revitAPIAssemblyFullName;

        public AssemLoader()
        {
            TempFolder = string.Empty;
            m_refedFolders = new List<string>();
            m_copiedFiles = new Dictionary<string, DateTime>();
        }

        // 保持原有的文件回写逻辑不变
        public void CopyGeneratedFilesBack()
        {
            if (!Directory.Exists(TempFolder)) return;
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
                // 注意：通常我们不希望把临时文件夹产生的所有垃圾文件都拷回源目录，视需求而定
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
            if (string.IsNullOrEmpty(originalFilePath) || !File.Exists(originalFilePath))
            {
                return null;
            }
            m_parsingOnly = parsingOnly;
            OriginalFolder = Path.GetDirectoryName(originalFilePath);

            var stringBuilder = new StringBuilder(Path.GetFileNameWithoutExtension(originalFilePath));
            stringBuilder.Append(parsingOnly ? "-Parsing-" : "-Executing-");

            // 1. 创建全新的临时文件夹 (基于时间戳，确保唯一)
            TempFolder = FileUtils.CreateTempFolder(stringBuilder.ToString());

            // 2. 复制并加载
            var assembly = CopyAndLoadAddin(originalFilePath, parsingOnly);
            if (null == assembly || !IsAPIReferenced(assembly))
            {
                return null;
            }
            return assembly;
        }

        private Assembly CopyAndLoadAddin(string srcFilePath, bool onlyCopyRelated)
        {
            string destPath = string.Empty;

            // 复制文件到临时目录
            if (!FileUtils.FileExistsInFolder(srcFilePath, TempFolder))
            {
                var directoryName = Path.GetDirectoryName(srcFilePath);
                if (!m_refedFolders.Contains(directoryName))
                {
                    m_refedFolders.Add(directoryName);
                }
                var list = new List<FileInfo>();
                destPath = FileUtils.CopyFileToFolder(srcFilePath, TempFolder, onlyCopyRelated, list);

                if (string.IsNullOrEmpty(destPath)) return null;

                foreach (var fileInfo in list)
                {
                    m_copiedFiles[fileInfo.FullName] = fileInfo.LastWriteTime;
                }
            }
            else
            {
                // 如果文件已存在（极少情况，因为是新Temp目录），构造目标路径
                destPath = Path.Combine(TempFolder, Path.GetFileName(srcFilePath));
            }

            // 加载复制后的文件
            return LoadAddin(destPath);
        }

        private Assembly LoadAddin(string filePath)
        {
            Assembly assembly = null;
            try
            {
                // 【核心修改】：使用字节流加载，而不是 LoadFile
                // 这样可以避免文件锁定，并且每次 Load 都会视为新的程序集实例
                if (File.Exists(filePath))
                {
                    byte[] assemblyBytes = File.ReadAllBytes(filePath);
                    byte[] pdbBytes = null;

                    // 尝试加载 PDB 以支持调试
                    string pdbPath = Path.ChangeExtension(filePath, "pdb");
                    if (File.Exists(pdbPath))
                    {
                        pdbBytes = File.ReadAllBytes(pdbPath);
                    }

                    // 使用 Load(bytes, pdbBytes)
                    assembly = Assembly.Load(assemblyBytes, pdbBytes);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAddin Failed: {filePath}, Error: {ex.Message}");
                throw;
            }
            return assembly;
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            // 防止递归或死循环
            string assemblyName = new AssemblyName(args.Name).Name;

            // 忽略资源文件
            if (assemblyName.EndsWith(".resources") || assemblyName.EndsWith(".XmlSerializers"))
                return null;

            // 1. 在临时文件夹中寻找依赖项
            // 因为主程序是字节流加载的，它不知道自己在 TempFolder，必须我们告诉它去那里找
            string foundPath = SearchAssemblyFileInTempFolder(assemblyName);

            if (!string.IsNullOrEmpty(foundPath))
            {
                // 找到依赖项后，同样使用字节流加载！
                // 这样保证主程序集和依赖程序集都在“无上下文”的环境中匹配
                return LoadAddin(foundPath);
            }

            // 2. 如果临时文件夹没有，去源文件夹找 (并复制过来)
            foundPath = SearchAssemblyFileInOriginalFolders(assemblyName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                return CopyAndLoadAddin(foundPath, true);
            }

            return null;
        }

        private string SearchAssemblyFileInTempFolder(string simpleName)
        {
            var extensions = new string[] { ".dll", ".exe" };
            foreach (var ext in extensions)
            {
                string path = Path.Combine(TempFolder, simpleName + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private string SearchAssemblyFileInOriginalFolders(string simpleName)
        {
            var extensions = new string[] { ".dll", ".exe" };

            // 1. 系统目录 (通常不需要，System dll 会自动解析，但保留以防万一)
            foreach (var ext in extensions)
            {
                string path = Path.Combine(m_dotnetDir, simpleName + ext);
                if (File.Exists(path)) return path;
            }

            // 2. 所有引用过的源文件夹
            foreach (var ext in extensions)
            {
                foreach (var folder in m_refedFolders)
                {
                    string path = Path.Combine(folder, simpleName + ext);
                    if (File.Exists(path)) return path;
                }
            }

            return null;
        }

        private bool IsAPIReferenced(Assembly assembly)
        {
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
            // 如果还没加载 RevitAPI (极其罕见)，通过
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