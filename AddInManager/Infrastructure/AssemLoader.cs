using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
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
        private static readonly Dictionary<string, AssemLoader> s_executionLoaders =
            new Dictionary<string, AssemLoader>(StringComparer.OrdinalIgnoreCase);
        private bool m_reuseUnchanged;
        private DateTime m_sourceWriteTimeUtc;
        private long m_sourceLength;
        private Assembly m_executionAssembly;

        public static string m_resolvedAssemPath = string.Empty;

        // 只读取类型、继承、接口和类型特性；方法体变化不会触发列表重载。
        internal static string ReadTypeDefinitionSignature(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var pe = new PEReader(stream))
            {
                var reader = pe.GetMetadataReader();
                var definitions = new List<string>();
                foreach (var handle in reader.TypeDefinitions)
                {
                    var type = reader.GetTypeDefinition(handle);
                    var interfaces = type.GetInterfaceImplementations()
                        .Select(h => GetMetadataTypeName(reader, reader.GetInterfaceImplementation(h).Interface))
                        .OrderBy(name => name, StringComparer.Ordinal);
                    var attributes = type.GetCustomAttributes().Select(h =>
                    {
                        var attribute = reader.GetCustomAttribute(h);
                        var owner = attribute.Constructor.Kind == HandleKind.MemberReference
                            ? reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
                            : reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
                        return GetMetadataTypeName(reader, owner) + ":" + Convert.ToBase64String(reader.GetBlobBytes(attribute.Value));
                    }).OrderBy(value => value, StringComparer.Ordinal);
                    definitions.Add(GetMetadataTypeName(reader, handle) + "|" + (int)type.Attributes + "|" +
                        GetMetadataTypeName(reader, type.BaseType) + "|" + string.Join(";", interfaces) + "|" + string.Join(";", attributes));
                }
                using (var hash = SHA256.Create())
                {
                    return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(
                        string.Join("\n", definitions.OrderBy(value => value, StringComparer.Ordinal)))));
                }
            }
        }

        private static string GetMetadataTypeName(MetadataReader reader, EntityHandle handle)
        {
            if (handle.IsNil) return string.Empty;
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    return GetMetadataTypeName(reader, definition.GetDeclaringType()) + "/" +
                        reader.GetString(definition.Namespace) + "." + reader.GetString(definition.Name);
                case HandleKind.TypeReference:
                    var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                    return GetMetadataTypeName(reader, reference.ResolutionScope) + "/" +
                        reader.GetString(reference.Namespace) + "." + reader.GetString(reference.Name);
                case HandleKind.AssemblyReference:
                    var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)handle);
                    return reader.GetString(assembly.Name) + "," + assembly.Version + "," + reader.GetString(assembly.Culture) +
                        "," + Convert.ToBase64String(reader.GetBlobBytes(assembly.PublicKeyOrToken));
                case HandleKind.TypeSpecification:
                    // 泛型签名含类型索引，同时核对索引映射，避免同一索引已指向不同基类或接口。
                    var names = reader.TypeReferences.Select(h => GetMetadataTypeName(reader, h))
                        .Concat(reader.TypeDefinitions.Select(h => GetMetadataTypeName(reader, h)));
                    return Convert.ToBase64String(reader.GetBlobBytes(reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature)) +
                        "[" + string.Join(";", names) + "]";
                default:
                    return handle.Kind.ToString();
            }
        }

        public AssemLoader()
        {
            TempFolder = string.Empty;
            m_refedFolders = new List<string>();
            m_copiedFiles = new Dictionary<string, DateTime>();
        }

        // 执行缓存仅保留每个插件的最新副本；关闭选项后恢复每次重新加载。
        public static AssemLoader GetExecutionLoader(string filePath, bool reuseUnchanged)
        {
            if (!reuseUnchanged)
            {
                s_executionLoaders.Clear();
                return new AssemLoader();
            }

            if (s_executionLoaders.TryGetValue(filePath, out var loader) && loader.CanReuse(filePath)) return loader;
            s_executionLoaders.Remove(filePath);
            return new AssemLoader { m_reuseUnchanged = true };
        }

        // 手动重载或移除插件时废弃对应缓存，下一次执行重新复制并加载。
        internal static void InvalidateExecutionCache(string filePath)
        {
            s_executionLoaders.Remove(filePath);
        }

        // 仅检查入口 DLL 的时间和大小，不扫描依赖及资源目录。
        private bool CanReuse(string filePath)
        {
            if (m_executionAssembly == null || !File.Exists(m_executionAssembly.Location)) return false;
            var file = new FileInfo(filePath);
            return file.Exists && file.LastWriteTimeUtc == m_sourceWriteTimeUtc && file.Length == m_sourceLength;
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
                        if (FileUtils.CopyFile(text, text3))
                        {
                            // 回写成功后同步临时文件时间，避免复用时反复回写。
                            m_copiedFiles[text] = fileInfo.LastWriteTime;
                        }
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
            if (!parsingOnly && m_executionAssembly != null)
            {
                DebugLogger.Instance.Info($"AssemLoader: Reusing unchanged assembly: {Path.GetFileName(originalFilePath)}");
                return m_executionAssembly;
            }
            DebugLogger.Instance.Info($"AssemLoader: Loading {System.IO.Path.GetFileName(originalFilePath)} (Parsing only: {parsingOnly})");
            m_parsingOnly = parsingOnly;
            OriginalFolder = Path.GetDirectoryName(originalFilePath);

            if (!parsingOnly && m_reuseUnchanged)
            {
                // 加载前记录，编译发生在复制期间时也会在下次执行中使缓存失效。
                var sourceFile = new FileInfo(originalFilePath);
                m_sourceWriteTimeUtc = sourceFile.LastWriteTimeUtc;
                m_sourceLength = sourceFile.Length;
            }

            var stringBuilder = new StringBuilder(Path.GetFileNameWithoutExtension(originalFilePath));
            stringBuilder.Append(parsingOnly ? "-Parsing-" : "-Executing-");

            TempFolder = FileUtils.CreateTempFolder(stringBuilder.ToString());

            // 【建议】在此处，除了复制主DLL，最好把同目录下的所有 .dll 都复制过去
            // 这样可以避免 AssemblyResolve 频繁触发，解决大部分 NuGet 依赖问题
            // CopyAllDllsToTemp(OriginalFolder, TempFolder); // 这是一个建议实现的辅助方法

            var assembly = CopyAndLoadAddin(originalFilePath, parsingOnly);
            // 解析允许不再引用 Revit API 的 DLL，以便自动核对能识别最后一个入口被删除。
            if (null == assembly || (!parsingOnly && !IsAPIReferenced(assembly)))
            {
                DebugLogger.Instance.Warning($"AssemLoader: Assembly does not reference Revit API or failed to load: {System.IO.Path.GetFileName(originalFilePath)}");
                return null;
            }
            if (!parsingOnly && m_reuseUnchanged)
            {
                m_executionAssembly = assembly;
                s_executionLoaders[originalFilePath] = this;
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
                // 使用 LoadFile 而非 LoadFrom：
                // LoadFrom 按 Identity (Name+Version+Culture+Token) 缓存，同 Identity 永远返回旧实例，热重载失效。
                // LoadFile 按文件路径隔离加载上下文：不同路径 = 不同实例，不受 Identity 缓存影响。
                // 每次 CreateTempFolder 生成新路径，LoadFile 就会加载最新编译的 DLL。
                // 同时 Assembly.Location 仍然正确指向 TempFolder 内的路径，插件读文件路径不受影响。
                // 依赖项解析由 AssemblyResolve 事件负责（LoadFile 不自动探测同目录依赖）。
                assembly = Assembly.LoadFile(filePath);
                DebugLogger.Instance.Info($"AssemLoader: Successfully loaded assembly: {System.IO.Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
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
