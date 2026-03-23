using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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
    }

    /// <summary>
    /// Analyzes addin assembly dependencies for the Dependency Graph window.
    /// The analysis is read-only and does not affect any core functionality.
    /// </summary>
    public class DependencyAnalyzer
    {
        /// <summary>
        /// Analyzes all loaded command and application addins and returns dependency nodes.
        /// Falls back to reading referenced assembly names from already-loaded AppDomain assemblies
        /// so no extra file I/O or assembly loading is performed.
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

            // Look for a matching assembly already loaded in the AppDomain
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var location = asm.IsDynamic ? string.Empty : asm.Location;
                    if (string.Equals(location, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        node.ReferencedAssemblies = asm
                            .GetReferencedAssemblies()
                            .Select(r => r.Name)
                            .OrderBy(n => n)
                            .ToList();
                        break;
                    }
                }
                catch
                {
                    // Skip assemblies that cannot be inspected
                }
            }

            return node;
        }
    }
}
