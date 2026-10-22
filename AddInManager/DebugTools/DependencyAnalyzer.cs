using AddInManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using AddInManager.Persistence;

namespace AddInManager.DebugTools
{
    /// <summary>
    /// Represents a single addin node in the dependency graph.
    /// </summary>
    public class DependencyNode
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public AddinType AddinType { get; set; }
        public List<string> ReferencedAssemblies { get; set; } = new List<string>();
        public string AnalysisError { get; set; }
    }

    /// <summary>
    /// Analyzes addin assembly dependencies for the Dependency Graph window.
    /// The analysis is read-only and does not affect any core functionality.
    /// </summary>
    public class DependencyAnalyzer
    {
        /// <summary>
        /// 读取管理器所记录 DLL 的直接引用，无需先执行插件，也不向宿主加载目标程序集。
        /// </summary>
        public List<DependencyNode> Analyze(AddinManager addinManager)
        {
            var nodes = new List<DependencyNode>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in addinManager.Commands.AddinDict)
            {
                if (seen.Add(kv.Value.FilePath))
                    nodes.Add(BuildNode(kv.Value, AddinType.Command));
            }

            foreach (var kv in addinManager.Applications.AddinDict)
            {
                if (seen.Add(kv.Value.FilePath))
                    nodes.Add(BuildNode(kv.Value, AddinType.Application));
            }

            return nodes;
        }

        private static DependencyNode BuildNode(Addin addin, AddinType type)
        {
            var filePath = addin.FilePath ?? string.Empty;
            var node = new DependencyNode
            {
                Name = string.IsNullOrEmpty(filePath)
                    ? "(unknown)"
                    : Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                AddinType = type
            };

            try
            {
                // 直接读取文件元数据，避免临时加载路径不匹配或读到重编译前的程序集。
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var peReader = new PEReader(stream))
                {
                    if (!peReader.HasMetadata) throw new System.BadImageFormatException();
                    var reader = peReader.GetMetadataReader();
                    if (!reader.IsAssembly) throw new System.BadImageFormatException();
                    node.ReferencedAssemblies = reader.AssemblyReferences
                        .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException ||
                                              ex is System.BadImageFormatException || ex is System.ArgumentException)
            {
                // 单个文件不可读时保留插件记录，并让界面明确显示失败原因。
                node.AnalysisError = ex.Message;
                DebugLogger.Instance.Error(ex, $"DependencyAnalyzer: {filePath}");
            }

            return node;
        }
    }
}
