using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using AddInManager.Core;
using AddInManager.Localization;
using AddInManager.Models;

using Microsoft.Win32;

namespace AddInManager.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly AIM m_aim;
        private List<TreeViewItem> m_allCommandItems; // 存储所有命令项用于搜索
        private DispatcherTimer m_searchDebounceTimer;
        private readonly DispatcherTimer m_assemblyCheckTimer;
        private readonly Dictionary<string, (DateTime WriteTimeUtc, bool Attempted)> m_assemblyUpdates =
            new Dictionary<string, (DateTime WriteTimeUtc, bool Attempted)>(StringComparer.OrdinalIgnoreCase);
        private bool m_closeAfterReload;
        private bool m_isLanguageSelectorInitializing = true;
        private bool m_isUpdatingExpansion;
        private bool m_isReloading;

        // Reusable virtualizing ItemsPanelTemplate for child nodes, created once to avoid per-node allocation
        private static readonly ItemsPanelTemplate s_virtualizingItemsPanel = CreateVirtualizingItemsPanel();

        private static ItemsPanelTemplate CreateVirtualizingItemsPanel()
        {
            var factory = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
            factory.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
            factory.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            factory.SetValue(ScrollViewer.CanContentScrollProperty, true);
            var template = new ItemsPanelTemplate { VisualTree = factory };
            template.Seal();
            return template;
        }

        public MainWindow(AIM aim)
        {
            InitializeComponent();
            Title = $"{Properties.Resources.WindowTitle} v{Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}";
            // Initialize language selector
            InitializeLanguageSelector();
            m_isLanguageSelectorInitializing = false;
            m_aim = aim;
            Loaded += MainWindow_Loaded;
            Activated += MainWindow_Activated;
            m_allCommandItems = new List<TreeViewItem>();

            // Debounce timer: filters the tree 300 ms after the user stops typing
            m_searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            m_searchDebounceTimer.Tick += OnSearchDebounceTimerTick;
            m_assemblyCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            m_assemblyCheckTimer.Tick += AssemblyCheckTimer_Tick;
            Closed += (sender, e) => m_assemblyCheckTimer.Stop();
            Closing += (sender, e) =>
            {
                if (!m_isReloading) return;
                e.Cancel = true;
                m_closeAfterReload = true;
            };
        }

        // 切回窗口时同步 DLL 文件状态，保留现有节点和选择。
        private void MainWindow_Activated(object sender, EventArgs e)
        {
            RefreshAssemblyStates();
            if (IsLoaded && !m_isReloading && externalToolsTabControl.SelectedIndex == 0) RefreshData();
        }

        // 定时检查仅更新文件状态，保留当前选择与列表。
        private void RefreshAssemblyStates()
        {
            if (!IsLoaded || m_isReloading) return;

            foreach (var tree in new[] { commandsTreeView, applicationsTreeView })
            {
                foreach (TreeViewItem node in tree.Items)
                {
                    RefreshAssemblyAvailability(node);
                }
            }
            if (externalToolsTabControl.SelectedIndex == 0 && m_aim.ActiveCmdItem != null)
            {
                runButton.IsEnabled = !m_isReloading && File.Exists(m_aim.ActiveCmd?.FilePath);
            }
        }

        // 两次检查间文件保持稳定才重载；失败后等待下一次写入，避免持续重试。
        private async void AssemblyCheckTimer_Tick(object sender, EventArgs e)
        {
            if (!IsLoaded || m_isReloading) return;
            RefreshAssemblyStates();

            try
            {
                var addins = m_aim.AddinManager.Commands.AddinDict.Values.Concat(m_aim.AddinManager.Applications.AddinDict.Values)
                    .GroupBy(addin => addin.FilePath, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
                foreach (var addin in addins)
                {
                    var writeTime = Addin.GetFileWriteTimeUtc(addin.FilePath);
                    if (!writeTime.HasValue || !addin.LoadedFileWriteTimeUtc.HasValue || writeTime == addin.LoadedFileWriteTimeUtc)
                    {
                        m_assemblyUpdates.Remove(addin.FilePath);
                        continue;
                    }
                    if (!m_assemblyUpdates.TryGetValue(addin.FilePath, out var update) || update.WriteTimeUtc != writeTime.Value)
                    {
                        m_assemblyUpdates[addin.FilePath] = (writeTime.Value, false);
                        continue;
                    }
                    if (update.Attempted) continue;
                    m_assemblyUpdates[addin.FilePath] = (writeTime.Value, true);
                    await ReloadAssemblyAsync(addin.FilePath, true);
                    if (!IsLoaded) return;
                }
            }
            catch (Exception ex)
            {
                DebugTools.DebugLogger.Instance.Error(ex, "AutoReload");
                ShowStatusError(string.Format(Properties.Resources.StatusReloadError, ex.Message));
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 每次打开窗口都取消保存勾选，不沿用历史配置或上次窗口的选择。
            foreach (var addins in new[] { m_aim.AddinManager.Commands.AddinDict.Values, m_aim.AddinManager.Applications.AddinDict.Values })
            {
                foreach (var addin in addins)
                {
                    addin.Save = false;
                    foreach (var item in addin.ItemList)
                    {
                        item.Save = false;
                    }
                }
            }

            CommandsTreeView_RefreshData();
            ApplicationsTreeView_RefreshData();
            DisableControl();
            removeButton.IsEnabled = false;
            m_assemblyCheckTimer.Start();

            if (searchTextBox.Template.FindName("PART_ClearButton", searchTextBox) is Button clearButton)
            {
                clearButton.Click += ClearSearchButton_Click;
            }

            if (commandsTreeView.Items.Count > 0)
            {
                if (commandsTreeView.Items[0] is TreeViewItem firstItem)
                {
                    firstItem.IsSelected = true;
                    firstItem.BringIntoView();
                    firstItem.Focus(); // 确保获得焦点
                }
            }
        }

        #region 搜索功能

        private void RecentCommandsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = recentCommandsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var commands = m_aim.AddinManager.Commands.AddinDict.Values.SelectMany(addin => addin.ItemList)
                .Where(command => command.LastRunUtc.HasValue)
                .OrderByDescending(command => command.LastRunUtc).Take(10).ToList();
            foreach (var command in commands)
            {
                var item = new MenuItem
                {
                    Header = new TextBlock { Text = $"{command.Name} — {Path.GetFileName(command.AssemblyPath)}" },
                    Tag = command,
                    IsEnabled = File.Exists(command.AssemblyPath),
                    ToolTip = $"{command.FullClassName}{Environment.NewLine}{command.AssemblyPath}{Environment.NewLine}{command.LastRunUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                };
                item.Click += RecentCommand_Click;
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = Properties.Resources.NoRecentCommands, IsEnabled = false });
            recentCommandsButton.ContextMenu = menu;
            menu.IsOpen = true;
        }

        // 点击最近记录即运行；按当前节点匹配，避免重载后仍执行历史对象。
        private void RecentCommand_Click(object sender, RoutedEventArgs e)
        {
            if (m_isReloading) return;
            if (sender is not MenuItem { Tag: AddinItem command }) return;
            foreach (TreeViewItem parent in commandsTreeView.Items)
            {
                if (parent.Tag is not Addin addin || !string.Equals(addin.FilePath, command.AssemblyPath, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (TreeViewItem child in parent.Items)
                {
                    if (child.Tag is not AddinItem item || item.FullClassName != command.FullClassName) continue;
                    searchTextBox.Clear();
                    m_searchDebounceTimer.Stop();
                    RestoreAllItems();
                    SetExpandedWithoutSaving(parent, true);
                    child.IsSelected = true;
                    child.BringIntoView();
                    child.Focus();
                    m_aim.ActiveCmd = addin;
                    m_aim.ActiveCmdItem = item;
                    Run();
                    return;
                }
            }
            ShowStatusError(string.Format(Properties.Resources.RunCommandNotFound, command.FullClassName, command.AssemblyPath));
        }

        #region 搜索功能

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Restart the debounce timer so filtering only fires after the user pauses typing
            m_searchDebounceTimer.Stop();
            m_searchDebounceTimer.Start();
        }

        private void OnSearchDebounceTimerTick(object sender, EventArgs e)
        {
            m_searchDebounceTimer.Stop();
            var searchText = searchTextBox.Text?.Trim().ToLower();

            if (string.IsNullOrEmpty(searchText))
            {
                // 如果搜索框为空，恢复所有项的初始状态
                RestoreAllItems();
            }
            else
            {
                // 执行搜索过滤
                FilterCommandsTreeView(searchText);
            }
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            searchTextBox.Clear();
            searchTextBox.Focus();
            RestoreAllItems();
        }

        // (重写) 核心过滤逻辑
        private void FilterCommandsTreeView(string searchText, TreeViewItem itemToFilter = null)
        {
            foreach (TreeViewItem parentItem in commandsTreeView.Items)
            {
                if (itemToFilter != null && parentItem != itemToFilter) continue;
                var parentShouldBeVisible = false;

                // 1. 检查父节点自身是否匹配
                var parentText = GetHeaderText(parentItem);
                var parentMatches = !string.IsNullOrEmpty(parentText) && parentText.ToLower().Contains(searchText);

                // 2. 遍历所有子节点
                foreach (TreeViewItem childItem in parentItem.Items)
                {
                    var childText = GetHeaderText(childItem);
                    var childMatches = !string.IsNullOrEmpty(childText) && childText.ToLower().Contains(searchText);

                    // 如果子节点匹配，或者父节点匹配，则该子节点可见
                    if (childMatches || parentMatches)
                    {
                        childItem.Visibility = Visibility.Visible;
                        parentShouldBeVisible = true; // 只要有任何一个子节点可见，父节点就必须可见
                    }
                    else
                    {
                        childItem.Visibility = Visibility.Collapsed;
                    }
                }

                // 3. 根据自身匹配或子节点可见性，来决定父节点的最终状态
                if (parentShouldBeVisible || parentMatches)
                {
                    parentItem.Visibility = Visibility.Visible;
                    SetExpandedWithoutSaving(parentItem, true); // 搜索临时展开，不覆盖手动保存的状态
                }
                else
                {
                    parentItem.Visibility = Visibility.Collapsed;
                }
            }
        }

        // (重写) 恢复所有项的可见性
        private void RestoreAllItems()
        {
            foreach (TreeViewItem parentItem in commandsTreeView.Items)
            {
                parentItem.Visibility = Visibility.Visible;
                if (parentItem.Tag is Addin addin)
                {
                    parentItem.IsExpanded = addin.IsExpanded;
                }

                foreach (TreeViewItem childItem in parentItem.Items)
                {
                    childItem.Visibility = Visibility.Visible;
                }
            }
        }

        // (新增) 辅助方法，用于从Header(StackPanel)中获取真实的文本
        private string GetHeaderText(TreeViewItem item)
        {
            if (item?.Header is StackPanel stackPanel)
            {
                // 遍历StackPanel中的所有子元素
                foreach (var child in stackPanel.Children)
                {
                    // 找到TextBlock并返回其文本
                    if (child is TextBlock textBlock)
                    {
                        return textBlock.Text;
                    }
                }
            }
            // 如果Header不是预期的格式，返回空字符串
            return string.Empty;
        }

        #endregion

        #endregion

        #region 工具栏功能

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllItemsSelection(commandsTreeView, true);
        }

        private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllItemsSelection(commandsTreeView, false);
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllItemsExpanded(commandsTreeView, true);
        }

        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllItemsExpanded(commandsTreeView, false);
        }
        private void AppSelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllItemsSelection(applicationsTreeView, true);
        }

        private void AppSelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllItemsSelection(applicationsTreeView, false);
        }
        private void AppExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllItemsExpanded(applicationsTreeView, true);
        }

        private void AppCollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllItemsExpanded(applicationsTreeView, false);
        }

        private void SetAllItemsExpanded(TreeView treeView, bool isExpanded)
        {
            var wasUpdating = m_isUpdatingExpansion;
            m_isUpdatingExpansion = true;
            try
            {
                foreach (TreeViewItem item in treeView.Items)
                {
                    SetTreeViewItemExpanded(item, isExpanded);
                    if (item.Tag is Addin addin) addin.IsExpanded = isExpanded;
                }
            }
            finally
            {
                m_isUpdatingExpansion = wasUpdating;
            }
            // 全部展开/折叠只写一次配置，避免逐节点重复写入。
            if (!m_isReloading) m_aim.AddinManager.SaveToAimIni();
        }

        private void TreeViewItem_ExpansionChanged(object sender, RoutedEventArgs e)
        {
            if (m_isUpdatingExpansion || e.OriginalSource != sender || sender is not TreeViewItem { Tag: Addin addin } item || addin.IsExpanded == item.IsExpanded) return;

            addin.IsExpanded = item.IsExpanded;
            if (!m_isReloading) m_aim.AddinManager.SaveToAimIni();
        }

        private void SetExpandedWithoutSaving(TreeViewItem item, bool isExpanded)
        {
            var wasUpdating = m_isUpdatingExpansion;
            m_isUpdatingExpansion = true;
            try { item.IsExpanded = isExpanded; }
            finally { m_isUpdatingExpansion = wasUpdating; }
        }

        private void SetTreeViewItemExpanded(TreeViewItem item, bool isExpanded)
        {
            if (item == null) return;

            item.IsExpanded = isExpanded;

            // 递归处理子项
            foreach (TreeViewItem childItem in item.Items)
            {
                SetTreeViewItemExpanded(childItem, isExpanded);
            }
        }

        private void SetAllItemsSelection(TreeView treeView, bool isSelected)
        {
            foreach (TreeViewItem parentItem in treeView.Items)
            {
                if (parentItem.Visibility == Visibility.Visible)
                {
                    // 设置父节点CheckBox
                    SetItemCheckBox(parentItem, isSelected);

                    if (parentItem.Tag is Addin addin)
                    {
                        addin.Save = isSelected;
                    }

                    foreach (TreeViewItem childItem in parentItem.Items)
                    {
                        if (childItem.Visibility == Visibility.Visible)
                        {
                            // 设置子节点CheckBox
                            SetItemCheckBox(childItem, isSelected);

                            if (childItem.Tag is AddinItem addinItem)
                            {
                                addinItem.Save = isSelected;
                            }
                        }
                    }
                }
            }

            // 保存更改
            m_aim.AddinManager.SaveToAimIni();

            ShowStatusLabel(isSelected ? Properties.Resources.StatusSelectedAll : Properties.Resources.StatusDeselectedAll);
        }

        private void InvertAllItemsSelection()
        {
            foreach (TreeViewItem parentItem in commandsTreeView.Items)
            {
                if (parentItem.Visibility == Visibility.Visible)
                {
                    // 反转父节点CheckBox
                    var currentState = GetItemCheckBoxState(parentItem);
                    SetItemCheckBox(parentItem, !currentState);

                    if (parentItem.Tag is Addin addin)
                    {
                        addin.Save = !addin.Save;
                    }

                    foreach (TreeViewItem childItem in parentItem.Items)
                    {
                        if (childItem.Visibility == Visibility.Visible)
                        {
                            // 反转子节点CheckBox
                            var childCurrentState = GetItemCheckBoxState(childItem);
                            SetItemCheckBox(childItem, !childCurrentState);

                            if (childItem.Tag is AddinItem addinItem)
                            {
                                addinItem.Save = !addinItem.Save;
                            }
                        }
                    }
                }
            }

            // 保存更改
            m_aim.AddinManager.SaveToAimIni();

            ShowStatusLabel(Properties.Resources.StatusInvertedSelection);
        }

        private void SetItemCheckBox(TreeViewItem item, bool isChecked)
        {
            var checkBox = FindCheckBoxInTreeViewItem(item);
            checkBox?.IsChecked = isChecked;
        }

        private bool GetItemCheckBoxState(TreeViewItem item)
        {
            var checkBox = FindCheckBoxInTreeViewItem(item);
            return checkBox?.IsChecked == true;
        }

        private CheckBox FindCheckBoxInTreeViewItem(TreeViewItem item)
        {
            if (item?.Header is StackPanel stackPanel)
            {
                foreach (var child in stackPanel.Children)
                {
                    if (child is CheckBox checkBox)
                    {
                        return checkBox;
                    }
                }
            }
            return null;
        }

        private void TreeViewCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox) return;

            var item = FindTreeViewItemFromCheckBox(checkBox);
            if (item?.Tag == null) return;

            // 区分是父节点还是子节点
            if (item.Tag is Addin addin)
            {
                // --- 父节点逻辑 ---
                var newState = checkBox.IsChecked;
                // 仅当父节点被用户明确点击为“选中”或“未选中”时，才更新所有子节点
                // 如果状态变为 null（不确定），则不执行任何操作，因为它是由子节点更新引起的
                if (newState.HasValue)
                {
                    var isChecked = newState.Value;
                    addin.Save = isChecked;
                    UpdateChildrenCheckBoxes(item, isChecked);
                }
                else
                {
                    // 当状态为不确定时，我们认为它仍然有需要保存的项
                    addin.Save = true;
                }
            }
            else if (item.Tag is AddinItem addinItem)
            {
                // --- 子节点逻辑 ---
                var isChecked = checkBox.IsChecked == true;
                addinItem.Save = isChecked;
                // 更新父节点的状态
                UpdateParentCheckBoxState(item);
            }

            // 保存更改到配置文件
            m_aim.AddinManager.SaveToAimIni();
        }

        private void UpdateChildrenCheckBoxes(TreeViewItem parentItem, bool isChecked)
        {
            // 遍历所有子项并更新它们的状态
            foreach (TreeViewItem childItem in parentItem.Items)
            {
                if (childItem.Tag is AddinItem addinItem)
                {
                    // 更新数据模型
                    addinItem.Save = isChecked;

                    // 更新UI（CheckBox）
                    var childCheckBox = FindCheckBoxInTreeViewItem(childItem);
                    if (childCheckBox != null)
                    {
                        // 临时移除事件处理程序，防止无限递归
                        childCheckBox.Checked -= TreeViewCheckBox_Changed;
                        childCheckBox.Unchecked -= TreeViewCheckBox_Changed;

                        childCheckBox.IsChecked = isChecked;

                        // 重新附加事件处理程序
                        childCheckBox.Checked += TreeViewCheckBox_Changed;
                        childCheckBox.Unchecked += TreeViewCheckBox_Changed;
                    }
                }
            }
        }
        private void UpdateParentCheckBoxState(TreeViewItem childItem)
        {
            if (childItem.Parent is TreeViewItem parentItem)
            {
                var allChecked = true;
                var anyChecked = false;

                // 检查所有子节点的状态
                foreach (TreeViewItem sibling in parentItem.Items)
                {
                    var siblingCheckBox = FindCheckBoxInTreeViewItem(sibling);
                    if (siblingCheckBox != null)
                    {
                        var isChecked = siblingCheckBox.IsChecked == true;
                        if (isChecked)
                        {
                            anyChecked = true;
                        }
                        else
                        {
                            allChecked = false;
                        }
                    }
                }

                // 更新父节点CheckBox状态
                var parentCheckBox = FindCheckBoxInTreeViewItem(parentItem);
                if (parentCheckBox != null)
                {
                    // 临时移除事件处理，避免递归调用
                    parentCheckBox.Checked -= TreeViewCheckBox_Changed;
                    parentCheckBox.Unchecked -= TreeViewCheckBox_Changed;

                    if (allChecked)
                    {
                        parentCheckBox.IsChecked = true;
                    }
                    else if (anyChecked)
                    {
                        parentCheckBox.IsChecked = null; // 部分选中状态
                    }
                    else
                    {
                        parentCheckBox.IsChecked = false;
                    }

                    // 重新添加事件处理
                    parentCheckBox.Checked += TreeViewCheckBox_Changed;
                    parentCheckBox.Unchecked += TreeViewCheckBox_Changed;
                }

                // 更新父节点数据模型
                if (parentItem.Tag is Addin parentAddin)
                {
                    parentAddin.Save = allChecked || anyChecked;
                }
            }
        }
        #endregion

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = Properties.Resources.LoadFileFilter
            };

            if (openFileDialog.ShowDialog() != true)
            {
                ShowStatusError(Properties.Resources.LoadCancelled);
                return;
            }

            var fileName = openFileDialog.FileName;

            // Disable load button and show progress while reflection-based loading runs in background
            loadButton.IsEnabled = false;
            ShowStatusLabel(string.Format(Properties.Resources.StatusLoading, Path.GetFileName(fileName)));

            AddinType addinType;
            try
            {
                addinType = await Task.Run(() => m_aim.AddinManager.LoadAddin(fileName));
            }
            catch (Exception ex)
            {
                loadButton.IsEnabled = true;
                ShowStatusError(string.Format(Properties.Resources.StatusLoadError, ex.Message));
                return;
            }
            finally
            {
                loadButton.IsEnabled = true;
            }

            if (addinType == AddinType.Invalid)
            {
                ShowStatusError(Properties.Resources.LoadFailed + fileName);
                return;
            }

            ShowStatusLabel(Properties.Resources.LoadSucceed + fileName);
            m_aim.AddinManager.SaveToAimIni();
            CommandsTreeView_RefreshData();
            ApplicationsTreeView_RefreshData();

            switch (addinType)
            {
                case AddinType.Command:
                case AddinType.Mixed:
                    externalToolsTabControl.SelectedItem = commandsTabPage;
                    commandsTreeView.Focus();
                    break;
                case AddinType.Application:
                    externalToolsTabControl.SelectedItem = applicationsTabPage;
                    applicationsTreeView.Focus();
                    break;
            }

            notesTextBox.Text = string.Empty;
            RemoveButton_RefreshData();
        }

        private void CommandsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as TreeViewItem;
            if (item?.Tag == null)
            {
                m_aim.ActiveCmd = null;
                m_aim.ActiveCmdItem = null;
                RefreshData();
                return;
            }

            if (item.Tag is Addin addin)
            {
                m_aim.ActiveCmd = addin;
                m_aim.ActiveCmdItem = null;
                notesTextBox.Text = m_aim.ActiveCmd.FilePath;
            }
            else if (item.Tag is AddinItem addinItem && item.Parent is TreeViewItem parentItem)
            {
                if (parentItem.Tag is Addin parentAddin)
                {
                    m_aim.ActiveCmd = parentAddin;
                    m_aim.ActiveCmdItem = addinItem;
                    notesTextBox.Text = m_aim.ActiveCmd.FilePath;
                }
            }

            RefreshData();
            RemoveButton_RefreshData();
        }

        private void ApplicationsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as TreeViewItem;
            if (item?.Tag == null)
            {
                m_aim.ActiveApp = null;
                m_aim.ActiveAppItem = null;
                DisableControl();
                RemoveButton_RefreshData();
                return;
            }

            if (item.Tag is Addin addin)
            {
                m_aim.ActiveApp = addin;
                m_aim.ActiveAppItem = null;
                notesTextBox.Text = m_aim.ActiveApp.FilePath;
            }
            else if (item.Tag is AddinItem addinItem && item.Parent is TreeViewItem parentItem)
            {
                if (parentItem.Tag is Addin parentAddin)
                {
                    m_aim.ActiveApp = parentAddin;
                    m_aim.ActiveAppItem = addinItem;
                    notesTextBox.Text = m_aim.ActiveApp.FilePath;
                }
            }

            DisableControl();
            RemoveButton_RefreshData();
        }

        private void ApplicationsTreeView_MouseUp(object sender, MouseButtonEventArgs e)
        {
            RemoveButton_RefreshData();
        }

        private void ApplicationsTreeView_LostFocus(object sender, RoutedEventArgs e)
        {
            // 添加 null 检查
            if (removeButton == null) return;

            // 检查焦点是否转移到了removeButton
            var focusedElement = Keyboard.FocusedElement as FrameworkElement;
            if (focusedElement != removeButton)
            {
                DisableControl();
                removeButton.IsEnabled = false;
            }
        }

        private void DisableControl()
        {
            // 添加 null 检查
            if (runButton == null) return;
            runButton.IsEnabled = false;
        }

        private void ExternalToolsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 添加 null 检查，防止在初始化期间访问
            if (externalToolsTabControl == null || applicationsTreeView == null || commandsTreeView == null) return;

            if (externalToolsTabControl.SelectedIndex == 1)
            {
                DisableControl();
                applicationsTreeView?.Focus();
                RemoveButton_RefreshData();
                return;
            }
            commandsTreeView?.Focus();
            RefreshData();
            RemoveButton_RefreshData(); // 添加这行
        }

        private void RefreshData()
        {
            if (commandsTreeView == null || runButton == null) return;

            if (commandsTreeView.SelectedItem is TreeViewItem selectedItem && !HasChildren(selectedItem))
            {
                runButton.IsEnabled = !m_isReloading && File.Exists(m_aim.ActiveCmd?.FilePath);
            }
            else
            {
                DisableControl();
            }
            RemoveButton_RefreshData();
        }

        private bool HasChildren(TreeViewItem item)
        {
            return item.Items.Count > 0;
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            Run();
        }

        private void Run()
        {
            if (m_isReloading) return;
            try
            {
                if (m_aim.ActiveCmdItem == null)
                {
                    ShowStatusError(Properties.Resources.StatusNoCommandSelected);
                    return;
                }

                // 文件可能在窗口打开后被移动或删除，执行前再次确认。
                if (!File.Exists(m_aim.ActiveCmd?.FilePath))
                {
                    RefreshData();
                    if (commandsTreeView.SelectedItem is TreeViewItem selected && selected.Parent is TreeViewItem parent)
                    {
                        RefreshAssemblyAvailability(parent);
                    }
                    ShowStatusError(string.Format(Properties.Resources.StatusAssemblyNotFound, m_aim.ActiveCmd?.FilePath));
                    return;
                }

                // 设置对话框结果为成功，这样AIM会知道用户选择了要执行命令
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowStatusError(string.Format(Properties.Resources.StatusRunError, ex.Message));
            }
        }

        // (重写并修复) 移除按钮点击事件
        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            TreeView activeTreeView;
            if (externalToolsTabControl.SelectedIndex == 0)
            {
                activeTreeView = commandsTreeView;
            }
            else if (externalToolsTabControl.SelectedIndex == 1)
            {
                activeTreeView = applicationsTreeView;
            }
            else
            {
                return;
            }

            if (activeTreeView.SelectedItem is not TreeViewItem selectedItem) return;

            // 1. 记录原始索引
            var originalIndex = -1;
            TreeViewItem parentItem = null;
            var isChildNode = false;

            // 判断是子节点还是父节点
            var parent = VisualTreeHelper.GetParent(selectedItem);
            while (parent is not null and not TreeView)
            {
                if (parent is TreeViewItem item)
                {
                    parentItem = item;
                    isChildNode = true;
                    break;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (isChildNode) // 如果是子节点
            {
                originalIndex = parentItem.Items.IndexOf(selectedItem);
            }
            else // 如果是父节点
            {
                originalIndex = activeTreeView.Items.IndexOf(selectedItem);
            }

            // 2. 执行数据移除
            if (externalToolsTabControl.SelectedIndex == 0)
            {
                m_aim.AddinManager.Commands.RemoveAddIn(m_aim.ActiveCmd);
                m_aim.ActiveCmd = null;
                m_aim.ActiveCmdItem = null;
            }
            else
            {
                m_aim.AddinManager.Applications.RemoveAddIn(m_aim.ActiveApp);
                m_aim.ActiveApp = null;
                m_aim.ActiveAppItem = null;
            }
            m_aim.AddinManager.SaveToAimIni();

            // 3. 刷新UI
            CommandsTreeView_RefreshData();
            ApplicationsTreeView_RefreshData();

            // 4. 计算新的安全索引并选中
            if (activeTreeView.Items.Count > 0)
            {
                // 简单处理：总是尝试选中一个有效的项
                var newIndex = Math.Max(0, Math.Min(originalIndex, activeTreeView.Items.Count - 1));

                if (activeTreeView.Items[newIndex] is TreeViewItem itemToSelect)
                {
                    itemToSelect.IsSelected = true;
                    itemToSelect.BringIntoView();
                    activeTreeView.Focus();
                }
            }
            else
            {
                // 如果列表空了，清空备注
                notesTextBox.Text = string.Empty;
            }

            RemoveButton_RefreshData();
        }

        private void RemoveButton_LostFocus(object sender, RoutedEventArgs e)
        {
            RemoveButton_RefreshData();
        }

        private void RemoveButton_RefreshData()
        {
            // 添加 null 检查，防止在初始化期间访问未初始化的控件
            if (removeButton == null || externalToolsTabControl == null) return;

            // 检查当前活动的标签页
            if (externalToolsTabControl.SelectedIndex == 0) // Commands tab
            {
                // 检查是否有选中的项目
                if (commandsTreeView.SelectedItem != null &&
                    (m_aim.ActiveCmd != null || m_aim.ActiveCmdItem != null))
                {
                    removeButton.IsEnabled = true;
                    if (m_aim.ActiveCmd != null)
                    {
                        notesTextBox.Text = m_aim.ActiveCmd.FilePath;
                    }
                }
                else
                {
                    removeButton.IsEnabled = false;
                }
            }
            else if (externalToolsTabControl.SelectedIndex == 1) // Applications tab
            {
                // 检查是否有选中的项目
                if (applicationsTreeView.SelectedItem != null &&
                    (m_aim.ActiveApp != null || m_aim.ActiveAppItem != null))
                {
                    removeButton.IsEnabled = true;
                    if (m_aim.ActiveApp != null)
                    {
                        notesTextBox.Text = m_aim.ActiveApp.FilePath;
                    }
                }
                else
                {
                    removeButton.IsEnabled = false;
                }
            }
            else
            {
                removeButton.IsEnabled = false;
            }
        }

        private void SaveSplitButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_aim.AddinManager.AppCount == 0 && m_aim.AddinManager.CmdCount == 0)
            {
                saveSplitButton.IsChecked = false;
                return;
            }

            saveContextMenu.PlacementTarget = saveSplitButton;
            saveContextMenu.IsOpen = saveSplitButton.IsChecked == true;
        }

        private void SaveContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            // 点击外部或按 Esc 关闭菜单后，同步复位下拉按钮。
            saveSplitButton.IsChecked = false;
        }


        private void CommandsTreeView_LostFocus(object sender, RoutedEventArgs e)
        {
            // 添加 null 检查
            if (removeButton == null || runButton == null) return;

            // 检查焦点是否转移到了相关控件
            var focusedElement = Keyboard.FocusedElement as FrameworkElement;
            if (focusedElement != runButton &&
                focusedElement != removeButton)
            {
                DisableControl();
                removeButton.IsEnabled = false;
            }
        }

        private void CommandsTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            // 使用辅助方法 FindTreeViewItem 查找被双击的 TreeViewItem
            var clickedItem = FindTreeViewItem(e.OriginalSource as DependencyObject);

            // 确保双击的是一个有效的 TreeViewItem，并且它是一个子节点（没有子项）
            if (clickedItem != null && !HasChildren(clickedItem))
            {
                // 确保选中的是可执行的命令项 (AddinItem)
                if (clickedItem.Tag is AddinItem)
                {
                    Run();
                }
                else
                {
                    ShowStatusError(Properties.Resources.StatusNotExecutableCommand);
                }
            }
            // 如果双击的是空白区域或父节点，则不执行任何操作
            //var selectedItem = commandsTreeView.SelectedItem as TreeViewItem;

            //if (selectedItem != null && !HasChildren(selectedItem))
            //{
            //    // 确保选中的是可执行的命令项
            //    if (m_aim.ActiveCmdItem != null)
            //    {
            //        Run();
            //    }
            //    else
            //    {
            //        ShowStatusError("选中的项目不是可执行的命令");
            //    }
            //}
        }

        private void SaveToAddinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var typeToSave = externalToolsTabControl.SelectedItem == commandsTabPage ? AddinType.Command : AddinType.Application;
            if (!m_aim.AddinManager.HasItemsToSave(typeToSave))
            {
                MessageBox.Show(Properties.Resources.NoItemsSelected, Properties.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (!string.IsNullOrEmpty(m_aim.AddinManager.SaveToAllUserManifest(typeToSave)))
            {
                Close();
            }
        }

        private void SaveLocalMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var typeToSave = externalToolsTabControl.SelectedItem == commandsTabPage ? AddinType.Command : AddinType.Application;
            if (!m_aim.AddinManager.HasItemsToSave(typeToSave))
            {
                MessageBox.Show(Properties.Resources.NoItemsSelected, Properties.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            var selectedAddins = (typeToSave == AddinType.Command ? m_aim.AddinManager.Commands.AddinDict.Values : m_aim.AddinManager.Applications.AddinDict.Values)
                .Where(a => a.Save && a.ItemList.Any(i => i.Save)).ToList();
            var dialog = new SaveFileDialog
            {
                Title = Properties.Resources.MenuSaveToLocal,
                Filter = Properties.Resources.AddinFileDialogFilter,
                DefaultExt = ".addin",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Path.GetDirectoryName(selectedAddins[0].FilePath),
                FileName = selectedAddins.Count == 1 ? Path.GetFileNameWithoutExtension(selectedAddins[0].FilePath) + ".addin" : "CombinedAddins.addin"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                m_aim.AddinManager.SaveToLocal(typeToSave, dialog.FileName);
                Close();
            }
            catch (Exception ex)
            {
                ShowStatusError(ex.Message);
            }
        }

        private void ShowStatusLabel(string msg)
        {
            notesTextBox.Foreground = Brushes.Black;
            notesTextBox.Text = msg;
        }

        private void ShowStatusError(string msg)
        {
            notesTextBox.Foreground = Brushes.Red;
            notesTextBox.Text = msg;
        }

        private void CommandsTreeView_RefreshData()
        {
            RefreshTreeView(commandsTreeView, m_aim.AddinManager.Commands);

            // 清除搜索框
            searchTextBox.Text = string.Empty;

            // 清空存储的命令项列表，防止重复
            m_allCommandItems.Clear();
        }

        private void ApplicationsTreeView_RefreshData()
        {
            RefreshTreeView(applicationsTreeView, m_aim.AddinManager.Applications);
        }

        private void RefreshTreeView(TreeView tree, Addins addins)
        {
            if (addins == null) return;

            // 清除现有项目，防止重复
            tree.Items.Clear();

            foreach (var kvp in addins.AddinDict)
            {
                tree.Items.Add(CreateAssemblyNode(kvp.Key, kvp.Value));
            }

            if (tree.Items.Count > 0)
            {
                if (tree.Items[tree.Items.Count - 1] is TreeViewItem lastItem)
                {
                    lastItem.IsSelected = true;
                    lastItem.BringIntoView();
                }
            }

            // 刷新RemoveButton状态
            RemoveButton_RefreshData();
        }
        // 重载只替换目标程序集节点，保留其他节点实例及当前视图状态。
        private void RefreshAssemblyNode(TreeView tree, Addins addins, string filePath)
        {
            TreeViewItem oldNode = null;
            foreach (TreeViewItem item in tree.Items)
            {
                if (item.Tag is Addin addin && string.Equals(addin.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    oldNode = item;
                    break;
                }
            }

            var key = Path.GetFileName(filePath);
            addins.AddinDict.TryGetValue(key, out var updatedAddin);
            if (oldNode == null && updatedAddin == null) return;

            var selected = tree.SelectedItem as TreeViewItem;
            var restoreSelection = oldNode != null && (selected == oldNode || oldNode.Items.Contains(selected));
            var selectedClass = (selected?.Tag as AddinItem)?.FullClassName;
            var wasExpanded = oldNode?.IsExpanded;
            var scroll = FindTreeScrollViewer(tree);
            var horizontalOffset = scroll?.HorizontalOffset ?? 0;
            var verticalOffset = scroll?.VerticalOffset ?? 0;
            var wasUpdating = m_isUpdatingExpansion;
            m_isUpdatingExpansion = true;
            try
            {
                if (updatedAddin == null)
                {
                    tree.Items.Remove(oldNode);
                }
                else
                {
                    if (oldNode?.Tag is Addin oldAddin)
                    {
                        updatedAddin.IsExpanded = oldAddin.IsExpanded;
                        // 已有命令沿用勾选；新增命令仍需手动勾选。
                        foreach (var item in updatedAddin.ItemList)
                        {
                            var previous = oldAddin.ItemList.Find(i => i.FullClassName == item.FullClassName);
                            if (previous != null) item.Save = previous.Save;
                        }
                        updatedAddin.Save = updatedAddin.ItemList.Exists(i => i.Save);
                    }

                    var node = CreateAssemblyNode(key, updatedAddin);
                    if (oldNode != null)
                    {
                        node.Visibility = oldNode.Visibility;
                        tree.Items[tree.Items.IndexOf(oldNode)] = node;
                    }
                    else
                    {
                        var index = 0;
                        foreach (var entry in addins.AddinDict)
                        {
                            if (entry.Key == key) break;
                            index++;
                        }
                        tree.Items.Insert(Math.Min(index, tree.Items.Count), node);
                    }

                    if (tree == commandsTreeView && !string.IsNullOrWhiteSpace(searchTextBox.Text))
                    {
                        FilterCommandsTreeView(searchTextBox.Text.Trim().ToLower(), node);
                    }
                    if (restoreSelection)
                    {
                        var itemToSelect = node;
                        foreach (TreeViewItem child in node.Items)
                        {
                            if (child.Tag is AddinItem item && item.FullClassName == selectedClass)
                            {
                                itemToSelect = child;
                                break;
                            }
                        }
                        itemToSelect.IsSelected = true;
                    }
                    if (wasExpanded.HasValue) node.IsExpanded = wasExpanded.Value;
                }
            }
            finally
            {
                m_isUpdatingExpansion = wasUpdating;
            }

            // 等待节点布局和选择引发的自动滚动结束后恢复原位置。
            if (scroll != null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsLoaded) return;
                    tree.UpdateLayout();
                    scroll.ScrollToHorizontalOffset(horizontalOffset);
                    scroll.ScrollToVerticalOffset(verticalOffset);
                }), DispatcherPriority.Background);
            }
            RemoveButton_RefreshData();
        }

        private ScrollViewer FindTreeScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer scroll) return scroll;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var result = FindTreeScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (result != null) return result;
            }
            return null;
        }

        private TreeViewItem CreateAssemblyNode(string key, Addin value)
        {
            var node = new TreeViewItem
            {
                Tag = value,
                IsExpanded = value.IsExpanded,
                // Use a VirtualizingStackPanel so child items are rendered on demand
                // rather than all at once, which significantly reduces lag with large lists
                ItemsPanel = s_virtualizingItemsPanel
            };
            node.Expanded += TreeViewItem_ExpansionChanged;
            node.Collapsed += TreeViewItem_ExpansionChanged;

            // 创建带CheckBox的Header
            var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var checkBox = new CheckBox
            {
                IsChecked = value.Save,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                IsThreeState = false // 父节点点击只允许两态（选中、未选中）
            };
            checkBox.Checked += TreeViewCheckBox_Changed;
            checkBox.Unchecked += TreeViewCheckBox_Changed;

            var textBlock = new TextBlock
            {
                Text = key,
                VerticalAlignment = VerticalAlignment.Center
            };


            stackPanel.Children.Add(checkBox);
            stackPanel.Children.Add(textBlock);
            node.Header = stackPanel;

            // 用于确定父节点初始状态
            var hasCheckedChildren = false;
            var hasUncheckedChildren = false;

            foreach (var addinItem in value.ItemList)
            {
                var childNode = new TreeViewItem
                {
                    Tag = addinItem
                };

                // 创建子节点的带CheckBox的Header
                var childStackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var childCheckBox = new CheckBox
                {
                    IsChecked = addinItem.Save,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                childCheckBox.Checked += TreeViewCheckBox_Changed;
                childCheckBox.Unchecked += TreeViewCheckBox_Changed;

                var childTextBlock = new TextBlock
                {
                    Text = addinItem.FullClassName,
                    VerticalAlignment = VerticalAlignment.Center
                };

                childStackPanel.Children.Add(childCheckBox);
                childStackPanel.Children.Add(childTextBlock);
                childNode.Header = childStackPanel;

                node.Items.Add(childNode);

                // 统计子节点状态
                if (addinItem.Save)
                {
                    hasCheckedChildren = true;
                }
                else
                {
                    hasUncheckedChildren = true;
                }
            }

            // 设置父节点CheckBox的初始状态
            if (hasCheckedChildren && hasUncheckedChildren)
            {
                checkBox.IsChecked = null; // 部分选中
            }
            else if (hasCheckedChildren)
            {
                checkBox.IsChecked = true; // 全选
            }
            else
            {
                checkBox.IsChecked = false; // 全不选
            }

            RefreshAssemblyAvailability(node);
            return node;
        }

        // 缺失文件仅禁用执行，节点仍可选择、移除和查看原位置。
        private void RefreshAssemblyAvailability(TreeViewItem node)
        {
            if (node.Tag is not Addin addin) return;

            var fileExists = File.Exists(addin.FilePath);
            if (!addin.LoadedFileWriteTimeUtc.HasValue) addin.LoadedFileWriteTimeUtc = Addin.GetFileWriteTimeUtc(addin.FilePath);
            var brushKey = fileExists ? "TextBrush" : "DisabledTextBrush";
            var tooltip = fileExists ? null : string.Format(Properties.Resources.StatusAssemblyNotFound, addin.FilePath);
            node.SetResourceReference(Control.ForegroundProperty, brushKey);
            node.ToolTip = tooltip;
            foreach (TreeViewItem child in node.Items)
            {
                child.SetResourceReference(Control.ForegroundProperty, brushKey);
                child.ToolTip = tooltip;
            }
        }

        // (新增) 辅助方法：从一个UI元素向上遍历可视化树，找到其所属的 TreeViewItem
        private TreeViewItem FindTreeViewItem(DependencyObject source)
        {
            while (source is not null and not TreeViewItem)
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as TreeViewItem;
        }
        // (重写并修复) 在右键菜单打开前，根据当前选中的项动态设置菜单项的可用状态
        private void CommandsTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 1. 默认将所有菜单项禁用，这是关键！
            ContextMenuRun.IsEnabled = false;
            ContextMenuLoad.IsEnabled = true; // "加载" 总是可用的
            ContextMenuRemove.IsEnabled = false;
            ContextMenuReload.IsEnabled = false;
            ContextMenuOpenInExplorer.IsEnabled = false;
            ContextMenuAssemblyInfo.IsEnabled = false;

            // 2. 准确查找鼠标右键点击的 TreeViewItem
            var clickedItem = FindTreeViewItem(e.OriginalSource as DependencyObject);

            // 3. 如果点击的是空白区域 (没有找到 TreeViewItem)，则直接返回
            if (clickedItem == null || clickedItem.Tag == null)
            {
                return;
            }

            // 4. 根据找到的项的类型，逐一启用对应的菜单项
            if (clickedItem.Tag is Addin addin)
            {
                // 当右键点击父节点 (Addin) 时
                ContextMenuRemove.IsEnabled = true;
                ContextMenuReload.IsEnabled = !m_isReloading && File.Exists(addin.FilePath);
                ContextMenuOpenInExplorer.IsEnabled = GetAssemblyLocation(addin.FilePath) != null;
            }
            else if (clickedItem.Tag is AddinItem addinItem)
            {
                // 当右键点击子节点 (AddinItem) 时
                ContextMenuRun.IsEnabled = !m_isReloading && File.Exists(addinItem.AssemblyPath);
                ContextMenuRemove.IsEnabled = true;
                ContextMenuReload.IsEnabled = !m_isReloading && File.Exists(addinItem.AssemblyPath);
                ContextMenuOpenInExplorer.IsEnabled = GetAssemblyLocation(addinItem.AssemblyPath) != null;
                ContextMenuAssemblyInfo.IsEnabled = File.Exists(addinItem.AssemblyPath);
            }
        }
        // 运行
        private void ContextMenuRun_Click(object sender, RoutedEventArgs e)
        {
            if (commandsTreeView.SelectedItem is TreeViewItem item && item.Tag is AddinItem)
            {
                Run();
            }
        }

        // 加载
        private void ContextMenuLoad_Click(object sender, RoutedEventArgs e)
        {
            // 直接调用已有的加载按钮逻辑
            LoadButton_Click(sender, e);
        }

        // 移除
        private void ContextMenuRemove_Click(object sender, RoutedEventArgs e)
        {
            // 直接调用已有的移除按钮逻辑
            if (commandsTreeView.SelectedItem != null)
            {
                RemoveButton_Click(sender, e);
            }
        }

        // (重写) 重新加载
        private async void ContextMenuReload_Click(object sender, RoutedEventArgs e)
        {
            if (m_isReloading) return;
            if (commandsTreeView.SelectedItem is not TreeViewItem selectedItem) return;

            Addin addinToReload = null;
            // 判断选中项是Addin(父)还是AddinItem(子)
            if (selectedItem.Tag is Addin addin)
            {
                addinToReload = addin;
            }
            else if (selectedItem.Tag is AddinItem)
            {
                // 如果是子节点，需要找到其父节点 Addin
                var parent = VisualTreeHelper.GetParent(selectedItem);
                while (parent != null)
                {
                    if (parent is TreeViewItem { Tag: Addin parentAddin })
                    {
                        addinToReload = parentAddin;
                        break;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            if (addinToReload == null)
            {
                ShowStatusError(Properties.Resources.StatusReloadNotFound);
                return;
            }

            await ReloadAssemblyAsync(addinToReload.FilePath);
        }

        private async Task ReloadAssemblyAsync(string filePath, bool automatic = false)
        {
            if (m_isReloading) return;
            if (!File.Exists(filePath))
            {
                ShowStatusError(string.Format(Properties.Resources.StatusReloadFileNotFound, filePath));
                return;
            }

            // 重载期间禁止执行，并在更新完成后处理关闭请求。
            m_isReloading = true;
            var content = Content as UIElement;
            var wasEnabled = content?.IsEnabled ?? true;
            if (content != null) content.IsEnabled = false;
            loadButton.IsEnabled = false;
            runButton.IsEnabled = false;
            if (!automatic) ShowStatusLabel(string.Format(Properties.Resources.StatusReloading, Path.GetFileName(filePath)));
            try
            {
                AddinType addinType;
                try
                {
                    addinType = await Task.Run(() => m_aim.AddinManager.LoadAddin(filePath));
                }
                catch (Exception ex)
                {
                    DebugTools.DebugLogger.Instance.Error(ex, $"ReloadAssembly: {filePath}");
                    m_aim.AddinManager.SaveToAimIni();
                    ShowStatusError(string.Format(Properties.Resources.StatusReloadError, ex.Message));
                    return;
                }

                if (addinType == AddinType.Invalid)
                {
                    m_aim.AddinManager.SaveToAimIni();
                    ShowStatusError(string.Format(Properties.Resources.StatusReloadFailed, filePath));
                }
                else
                {
                    var key = Path.GetFileName(filePath);
                    if ((addinType & AddinType.Command) == 0) m_aim.AddinManager.Commands.AddinDict.Remove(key);
                    if ((addinType & AddinType.Application) == 0) m_aim.AddinManager.Applications.AddinDict.Remove(key);
                    RefreshAssemblyNode(commandsTreeView, m_aim.AddinManager.Commands, filePath);
                    RefreshAssemblyNode(applicationsTreeView, m_aim.AddinManager.Applications, filePath);
                    m_aim.AddinManager.SaveToAimIni();
                    if (!automatic) ShowStatusLabel(string.Format(Properties.Resources.StatusReloadSuccess, filePath));
                }
            }
            finally
            {
                m_isReloading = false;
                if (content != null) content.IsEnabled = wasEnabled;
                loadButton.IsEnabled = true;
                if (IsLoaded && externalToolsTabControl.SelectedIndex == 0) RefreshData();
                if (m_closeAfterReload) Close();
            }
        }

        // 在资源管理器中显示
        private void ContextMenuOpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var filePath = string.Empty;
            if (commandsTreeView.SelectedItem is TreeViewItem item)
            {
                if (item.Tag is Addin addin)
                {
                    filePath = addin.FilePath;
                }
                else if (item.Tag is AddinItem addinItem)
                {
                    filePath = addinItem.AssemblyPath;
                }
            }

            var location = GetAssemblyLocation(filePath);
            if (location != null)
            {
                try
                {
                    // DLL 缺失时打开仍存在的原目录。
                    var arguments = File.Exists(location) ? $"/select,\"{location}\"" : $"\"{location}\"";
                    Process.Start("explorer.exe", arguments);
                }
                catch (Exception ex)
                {
                    ShowStatusError(string.Format(Properties.Resources.StatusOpenLocationError, ex.Message));
                }
            }
            else
            {
                ShowStatusError(Properties.Resources.StatusInvalidPath);
            }
        }

        private static string GetAssemblyLocation(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            if (File.Exists(filePath)) return filePath;

            var directory = Path.GetDirectoryName(filePath);
            return Directory.Exists(directory) ? directory : null;
        }

        // 查看程序集信息
        private void ContextMenuAssemblyInfo_Click(object sender, RoutedEventArgs e)
        {
            if (commandsTreeView.SelectedItem is TreeViewItem item && item.Tag is AddinItem addinItem)
            {
                var filePath = addinItem.AssemblyPath;
                if (!File.Exists(filePath))
                {
                    ShowStatusError(string.Format(Properties.Resources.StatusAssemblyNotFound, filePath));
                    return;
                }

                try
                {
                    // 注意：LoadFrom会锁定文件。对于更复杂的场景，可能需要使用不同的加载上下文。
                    var assembly = Assembly.LoadFrom(filePath);
                    var assemblyName = assembly.GetName();
                    var referencedAssemblies = assembly.GetReferencedAssemblies();

                    var info = new System.Text.StringBuilder();
                    info.AppendLine(string.Format(Properties.Resources.AssemblyInfoAssembly, assemblyName.Name));
                    info.AppendLine(string.Format(Properties.Resources.AssemblyInfoVersion, assemblyName.Version));
                    info.AppendLine(string.Format(Properties.Resources.AssemblyInfoFullName, assembly.FullName));
                    info.AppendLine(Properties.Resources.AssemblyInfoDependencies);

                    foreach (var refAssembly in referencedAssemblies)
                    {
                        info.AppendLine(refAssembly.FullName);
                    }

                    MessageBox.Show(info.ToString(), Properties.Resources.AssemblyInfoTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowStatusError(string.Format(Properties.Resources.StatusAssemblyLoadError, ex.Message));
                }
            }
        }
        private TreeViewItem FindTreeViewItemFromCheckBox(CheckBox checkBox)
        {
            DependencyObject parent = checkBox;

            // 向上遍历可视化树，直到找到TreeViewItem
            while (parent != null)
            {
                parent = VisualTreeHelper.GetParent(parent);
                if (parent is TreeViewItem treeViewItem)
                {
                    return treeViewItem;
                }
            }

            // 如果通过可视化树没找到，尝试通过逻辑树查找
            parent = checkBox;
            while (parent != null)
            {
                parent = LogicalTreeHelper.GetParent(parent);
                if (parent is TreeViewItem treeViewItem)
                {
                    return treeViewItem;
                }
            }

            return null;
        }

        // (新增) 在右键按下时，强制选中鼠标下的TreeViewItem
        private void CommandsTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeView treeView) return;

            var hitTestResult = VisualTreeHelper.HitTest(treeView, e.GetPosition(treeView));
            if (hitTestResult == null) return;

            var dependencyObject = hitTestResult.VisualHit;
            while (dependencyObject != null)
            {
                if (dependencyObject is TreeViewItem item)
                {
                    // 找到了TreeViewItem，将其设为选中状态
                    item.IsSelected = true;
                    e.Handled = true; // 阻止事件继续传播，防止其他意外行为
                    return;
                }
                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
            }
        }

        private void InitializeLanguageSelector()
        {
            foreach (ComboBoxItem item in languageComboBox.Items)
            {
                if (item.Tag?.ToString() == LanguageManager.CurrentCultureName)
                {
                    languageComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_isLanguageSelectorInitializing)
            {
                return;
            }

            if (languageComboBox.SelectedItem is ComboBoxItem selected)
            {
                var cultureName = selected.Tag?.ToString();
                if (!string.IsNullOrEmpty(cultureName) && cultureName != LanguageManager.CurrentCultureName)
                {
                    LanguageManager.SetLanguage(cultureName);
                    App.RefreshRibbonLanguage();
                    Dispatcher.BeginInvoke(new Action(() => Close()), DispatcherPriority.Background);
                }
            }
        }
    }
}
