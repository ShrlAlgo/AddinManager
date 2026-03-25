using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

using AddInManager.DebugTools;

namespace AddInManager.Wpf
{
    /// <summary>
    /// View model adapter to present a DependencyNode in the addin list.
    /// Keeps the core DependencyNode model clean from WPF concerns.
    /// </summary>
    internal class DependencyNodeViewModel
    {
        private readonly DependencyNode _node;

        public DependencyNodeViewModel(DependencyNode node) { _node = node; }

        public string Name      => _node.Name;
        public string FilePath  => _node.FilePath;
        public AddinType AddinType => _node.AddinType;
        public string AddinTypeLabel
        {
            get
            {
                switch (_node.AddinType)
                {
                    case AddinType.Command:     return "[Cmd]";
                    case AddinType.Application: return "[App]";
                    default:                    return $"[{_node.AddinType}]";
                }
            }
        }
        public List<string> ReferencedAssemblies => _node.ReferencedAssemblies;
    }

    public partial class DependencyGraphWindow : Window
    {
        private readonly AIM _aim;
        private readonly DependencyAnalyzer _analyzer = new DependencyAnalyzer();

        public DependencyGraphWindow(AIM aim)
        {
            _aim = aim;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RunAnalysis();
        }

        private void RunAnalysis()
        {
            addinListView.Items.Clear();
            depsListView.Items.Clear();
            filePathText.Text = string.Empty;

            var nodes = _analyzer.Analyze(_aim.AddinManager);

            foreach (var node in nodes)
                addinListView.Items.Add(new DependencyNodeViewModel(node));

            statusText.Text = string.Format(Properties.Resources.DependencyAnalyzed, nodes.Count);
            DebugLogger.Instance.Info($"[DependencyAnalyzer] 已分析 {nodes.Count} 个插件");
        }

        private void AddinListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (addinListView.SelectedItem is DependencyNodeViewModel vm)
            {
                detailHeader.Text = string.Format(Properties.Resources.DependencyDetailHeader, vm.Name, vm.ReferencedAssemblies.Count);
                filePathText.Text = vm.FilePath;

                depsListView.Items.Clear();
                foreach (var dep in vm.ReferencedAssemblies)
                    depsListView.Items.Add(dep);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RunAnalysis();
        }
    }
}
