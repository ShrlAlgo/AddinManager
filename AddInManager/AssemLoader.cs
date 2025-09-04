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

        public AssemLoader()
        {
            TempFolder = string.Empty;
            m_refedFolders = new List<string>();
            m_copiedFiles = new Dictionary<string, DateTime>();
        }

        public void CopyGeneratedFilesBack()
        {
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
                else
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
            if (parsingOnly)
            {
                stringBuilder.Append("-Parsing-");
            }
            else
            {
                stringBuilder.Append("-Executing-");
            }
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
            var text = string.Empty;
            if (!FileUtils.FileExistsInFolder(srcFilePath, TempFolder))
            {
                var directoryName = Path.GetDirectoryName(srcFilePath);
                if (!m_refedFolders.Contains(directoryName))
                {
                    m_refedFolders.Add(directoryName);
                }
                var list = new List<FileInfo>();
                text = FileUtils.CopyFileToFolder(srcFilePath, TempFolder, onlyCopyRelated, list);
                if (string.IsNullOrEmpty(text))
                {
                    return null;
                }
                foreach (var fileInfo in list)
                {
                    m_copiedFiles.Add(fileInfo.FullName, fileInfo.LastWriteTime);
                }
            }
            return LoadAddin(text);
        }

        private Assembly LoadAddin(string filePath)
        {
            Assembly assembly = null;
            try
            {
                Monitor.Enter(this);
                assembly = Assembly.LoadFile(filePath);
            }
            finally
            {
                Monitor.Exit(this);
            }
            return assembly;
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            Assembly assembly;
            lock (this)
            {
                new AssemblyName(args.Name);
                var text = SearchAssemblyFileInTempFolder(args.Name);
                if (File.Exists(text))
                {
                    assembly = LoadAddin(text);
                }
                else
                {
                    text = SearchAssemblyFileInOriginalFolders(args.Name);
                    if (string.IsNullOrEmpty(text))
                    {
                        var array = args.Name.Split(new char[] { ',' });
                        var text2 = array[0];
                        if (array.Length > 1)
                        {
                            var text3 = array[2];
                            if (text2.EndsWith(".resources", StringComparison.CurrentCultureIgnoreCase) && !text3.EndsWith("neutral", StringComparison.CurrentCultureIgnoreCase))
                            {
                                text2 = text2.Substring(0, text2.Length - ".resources".Length);
                            }
                            text = SearchAssemblyFileInTempFolder(text2);
                            if (File.Exists(text))
                            {
                                return LoadAddin(text);
                            }
                            text = SearchAssemblyFileInOriginalFolders(text2);
                        }
                    }
                    if (string.IsNullOrEmpty(text))
                    {
                        var assemblySelector = new Wpf.AssemblySelectorWindow(args.Name);
                        if (assemblySelector.ShowDialog() != true)
                        {
                            return null;
                        }
                        text = assemblySelector.ResultPath;
                    }
                    assembly = CopyAndLoadAddin(text, true);
                }
            }
            return assembly;
        }

        private string SearchAssemblyFileInTempFolder(string assemName)
        {
            var array = new string[] { ".dll", ".exe" };
            var text = string.Empty;
            var text2 = assemName.Substring(0, assemName.IndexOf(','));
            foreach (var text3 in array)
            {
                text = $"{TempFolder}\\{text2}{text3}";
                if (File.Exists(text))
                {
                    return text;
                }
            }
            return string.Empty;
        }

        private string SearchAssemblyFileInOriginalFolders(string assemName)
        {
            var array = new string[] { ".dll", ".exe" };
            var text = string.Empty;
            var text2 = assemName.Substring(0, assemName.IndexOf(','));
            foreach (var text3 in array)
            {
                text = $"{m_dotnetDir}\\{text2}{text3}";
                if (File.Exists(text))
                {
                    return text;
                }
            }
            foreach (var text4 in array)
            {
                foreach (var text5 in m_refedFolders)
                {
                    text = $"{text5}\\{text2}{text4}";
                    if (File.Exists(text))
                    {
                        return text;
                    }
                }
            }
            try
            {
                var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName));
                var text6 = $"{directoryInfo.Parent.FullName}\\Regression\\_RegressionTools\\";
                if (Directory.Exists(text6))
                {
                    foreach (var text7 in Directory.GetFiles(text6, "*.*", SearchOption.AllDirectories))
                    {
                        if (Path.GetFileNameWithoutExtension(text7).Equals(text2, StringComparison.OrdinalIgnoreCase))
                        {
                            return text7;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            var num = assemName.IndexOf("XMLSerializers", StringComparison.OrdinalIgnoreCase);
            if (num != -1)
            {
                assemName = $"System.XML{assemName.Substring(num + "XMLSerializers".Length)}";
                return SearchAssemblyFileInOriginalFolders(assemName);
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
            foreach (var assemblyName in assembly.GetReferencedAssemblies())
            {
                if (m_revitAPIAssemblyFullName == assemblyName.Name)
                {
                    return true;
                }
            }
            return false;
        }

        private List<string> m_refedFolders;

        private Dictionary<string, DateTime> m_copiedFiles;

        private bool m_parsingOnly;

        private static string m_dotnetDir =
            $"{Environment.GetEnvironmentVariable("windir")}\\Microsoft.NET\\Framework\\v2.0.50727";

        public static string m_resolvedAssemPath = string.Empty;

        private string m_revitAPIAssemblyFullName;
    }
}
