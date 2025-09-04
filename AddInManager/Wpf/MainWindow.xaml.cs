using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Microsoft.Win32;

namespace AddInManager.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly AIM m_aim;
        private List<TreeViewItem> m_allCommandItems; // 存储所有命令项用于搜索

        public MainWindow(AIM aim)
        {
            aim.AddinManager.Commands?.AddinDict?.Clear();
            aim.AddinManager.Applications?.AddinDict?.Clear();
            aim.AddinManager.ReadAddinsFromAimIni();

            InitializeComponent();
            m_aim = aim;
            Title = Properties.Resources.AppName;
            Loaded += MainWindow_Loaded;
            m_allCommandItems = new List<TreeViewItem>();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CommandsTreeView_RefreshData();
            ApplicationsTreeView_RefreshData();
            DisableControl();
            removeButton.IsEnabled = false;

            if (searchTextBox.Template.FindName("PART_ClearButton", searchTextBox) is Button clearButton)
            {
                clearButton.Click += ClearSearchButton_Click;
            }

            if (commandsTreeView.Items.Count > 0)
            {
                var firstItem = commandsTreeView.Items[0] as TreeViewItem;
                if (firstItem != null)
                {
                    firstItem.IsSelected = true;
                    firstItem.BringIntoView();
                    firstItem.Focus(); // 确保获得焦点
                }
            }
        }

        #region 搜索功能

        #region 搜索功能

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
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
        private void FilterCommandsTreeView(string searchText)
        {
            foreach (TreeViewItem parentItem in commandsTreeView.Items)
            {
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
                    parentItem.IsExpanded = true; // 展开以显示匹配的子项
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
                // 注意：这里我们不再强制展开所有项，除非您希望如此。
                // 如果需要恢复时全部展开，可以取消下面这行的注释。
                // parentItem.IsExpanded = true;

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
            foreach (TreeViewItem item in treeView.Items)
            {
                SetTreeViewItemExpanded(item, isExpanded);
            }
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

            ShowStatusLabel(isSelected ? "已选择所有可见项目" : "已取消选择所有项目");
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

            ShowStatusLabel("已反转所有可见项目的选择状态");
        }

        private void SetItemCheckBox(TreeViewItem item, bool isChecked)
        {
            var checkBox = FindCheckBoxInTreeViewItem(item);
            if (checkBox != null)
            {
                checkBox.IsChecked = isChecked;
            }
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
            if (sender is CheckBox checkBox)
            {
                var item = FindTreeViewItemFromCheckBox(checkBox);
                if (item == null) return;

                var isChecked = checkBox.IsChecked == true;

                // 更新对应的数据模型
                if (item.Tag is Addin addin)
                {
                    addin.Save = isChecked;

                    // 父节点状态变化时，同步更新所有子节点
                    UpdateChildrenCheckBoxes(item, isChecked);
                }
                else if (item.Tag is AddinItem addinItem)
                {
                    addinItem.Save = isChecked;

                    // 子节点状态变化时，检查是否需要更新父节点状态
                    UpdateParentCheckBoxState(item);
                }

                // 保存更改
                m_aim.AddinManager.SaveToAimIni();
            }
        }

        private void UpdateChildrenCheckBoxes(TreeViewItem parentItem, bool isChecked)
        {
            foreach (TreeViewItem childItem in parentItem.Items)
            {
                // 更新子节点CheckBox状态
                var childCheckBox = FindCheckBoxInTreeViewItem(childItem);
                if (childCheckBox != null)
                {
                    // 临时移除事件处理，避免递归调用
                    childCheckBox.Checked -= TreeViewCheckBox_Changed;
                    childCheckBox.Unchecked -= TreeViewCheckBox_Changed;

                    childCheckBox.IsChecked = isChecked;

                    // 重新添加事件处理
                    childCheckBox.Checked += TreeViewCheckBox_Changed;
                    childCheckBox.Unchecked += TreeViewCheckBox_Changed;
                }

                // 更新子节点数据模型
                if (childItem.Tag is AddinItem addinItem)
                {
                    addinItem.Save = isChecked;
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

        private void LoadButton_Click(object sender, RoutedEventArgs e)
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
            var addinType = m_aim.AddinManager.LoadAddin(fileName);

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
            nametextBox.Text = "";
            descriptionTextBox.Text = "";
            nametextBox.IsEnabled = false;
            descriptionTextBox.IsEnabled = false;
            runButton.IsEnabled = false;
        }

        private void ExternalToolsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (externalToolsTabControl.SelectedIndex == 1)
            {
                DisableControl();
                applicationsTreeView.Focus();
                RemoveButton_RefreshData();
                return;
            }
            commandsTreeView.Focus();
            RefreshData();
            RemoveButton_RefreshData(); // 添加这行
        }

        private void RefreshData()
        {
            var selectedItem = commandsTreeView.SelectedItem as TreeViewItem;
            if (selectedItem != null && !HasChildren(selectedItem))
            {
                if (m_aim.ActiveCmdItem != null)
                {
                    nametextBox.Text = m_aim.ActiveCmdItem.Name;
                    descriptionTextBox.Text = m_aim.ActiveCmdItem.Description;
                }
                nametextBox.IsEnabled = true;
                descriptionTextBox.IsEnabled = true;
                runButton.IsEnabled = true;
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
            try
            {
                if (m_aim.ActiveCmdItem == null)
                {
                    ShowStatusError("没有选中可执行的命令");
                    return;
                }

                // 设置对话框结果为成功，这样AIM会知道用户选择了要执行命令
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowStatusError($"准备执行命令时发生错误: {ex.Message}");
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

            if (!(activeTreeView.SelectedItem is TreeViewItem selectedItem)) return;

            // 1. 记录原始索引
            var originalIndex = -1;
            TreeViewItem parentItem = null;
            var isChildNode = false;

            // 判断是子节点还是父节点
            var parent = VisualTreeHelper.GetParent(selectedItem);
            while (parent != null && !(parent is TreeView))
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
            //// 检查是否有可保存项的逻辑 (如果需要)
            //if (!m_aim.AddinManager.HasItemsToSave())
            //{
            //    // 也可以选择在这里禁用按钮，而不是显示消息框
            //    // MessageBox.Show(...); 
            //    return;
            //}

            if (m_aim.AddinManager.AppCount != 0 || m_aim.AddinManager.CmdCount != 0)
            {
                saveContextMenu.PlacementTarget = saveSplitButton;
                saveContextMenu.IsOpen = true;
            }
        }

        private void NameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (m_aim.ActiveCmdItem != null)
            {
                m_aim.ActiveCmdItem.Name = nametextBox.Text;
                m_aim.AddinManager.SaveToAimIni();
            }
        }

        private void DescriptionTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (m_aim.ActiveCmdItem != null)
            {
                m_aim.ActiveCmdItem.Description = descriptionTextBox.Text;
                m_aim.AddinManager.SaveToAimIni();
            }
        }


        private void CommandsTreeView_LostFocus(object sender, RoutedEventArgs e)
        {
            // 检查焦点是否转移到了相关控件
            var focusedElement = Keyboard.FocusedElement as FrameworkElement;
            if (focusedElement != nametextBox &&
                focusedElement != descriptionTextBox &&
                focusedElement != runButton &&
                focusedElement != removeButton)
            {
                DisableControl();
                removeButton.IsEnabled = false;
            }
        }

        private void CommandsTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var selectedItem = commandsTreeView.SelectedItem as TreeViewItem;
            if (selectedItem != null && !HasChildren(selectedItem))
            {
                // 确保选中的是可执行的命令项
                if (m_aim.ActiveCmdItem != null)
                {
                    Run();
                }
                else
                {
                    ShowStatusError("选中的项目不是可执行的命令");
                }
            }
        }

        private void SaveToAddinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!m_aim.AddinManager.HasItemsToSave())
            {
                MessageBox.Show(Properties.Resources.NoItemsSelected, Properties.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            m_aim.AddinManager.SaveToAllUserManifest();
            ShowStatusLabel("保存成功，请关闭窗口加载插件");
        }

        private void SaveLocalMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!m_aim.AddinManager.HasItemsToSave())
            {
                MessageBox.Show(Properties.Resources.NoItemsSelected, Properties.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            m_aim.AddinManager.SaveToLocal();
            ShowStatusLabel("保存成功");
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
                var key = kvp.Key;
                var value = kvp.Value;

                var node = new TreeViewItem
                {
                    Tag = value,
                    IsExpanded = true // 默认展开
                };

                // 创建带CheckBox的Header
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var checkBox = new CheckBox
                {
                    IsChecked = value.Save,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                    IsThreeState = true // 支持三态（选中、未选中、部分选中）
                };
                checkBox.Checked += TreeViewCheckBox_Changed;
                checkBox.Unchecked += TreeViewCheckBox_Changed;
                checkBox.Indeterminate += TreeViewCheckBox_Changed;

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

                tree.Items.Add(node);
            }

            if (tree.Items.Count > 0)
            {
                var lastItem = tree.Items[tree.Items.Count - 1] as TreeViewItem;
                if (lastItem != null)
                {
                    lastItem.IsSelected = true;
                    lastItem.BringIntoView();
                }
            }

            // 刷新RemoveButton状态
            RemoveButton_RefreshData();
        }
        // (新增) 辅助方法：从一个UI元素向上遍历可视化树，找到其所属的 TreeViewItem
        private TreeViewItem FindTreeViewItem(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
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
                ContextMenuReload.IsEnabled = File.Exists(addin.FilePath);
                ContextMenuOpenInExplorer.IsEnabled = File.Exists(addin.FilePath);
            }
            else if (clickedItem.Tag is AddinItem addinItem)
            {
                // 当右键点击子节点 (AddinItem) 时
                ContextMenuRun.IsEnabled = true;
                ContextMenuRemove.IsEnabled = true;
                ContextMenuReload.IsEnabled = File.Exists(addinItem.AssemblyPath);
                ContextMenuOpenInExplorer.IsEnabled = File.Exists(addinItem.AssemblyPath);
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
        private void ContextMenuReload_Click(object sender, RoutedEventArgs e)
        {
            if (!(commandsTreeView.SelectedItem is TreeViewItem selectedItem)) return;

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
                    if (parent is TreeViewItem parentItem && parentItem.Tag is Addin parentAddin)
                    {
                        addinToReload = parentAddin;
                        break;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            if (addinToReload == null)
            {
                ShowStatusError("无法找到插件信息进行重新加载。");
                return;
            }

            var filePath = addinToReload.FilePath;
            if (!File.Exists(filePath))
            {
                ShowStatusError($"插件文件不存在，无法重新加载: {filePath}");
                return;
            }

            // 1. 从数据模型中移除
            m_aim.AddinManager.Commands.RemoveAddIn(addinToReload);
            m_aim.AddinManager.SaveToAimIni();

            // 2. 重新加载该文件
            var addinType = m_aim.AddinManager.LoadAddin(filePath);

            // 3. 刷新整个UI
            CommandsTreeView_RefreshData();
            ApplicationsTreeView_RefreshData();

            if (addinType == AddinType.Invalid)
            {
                ShowStatusError($"重新加载失败: {filePath}");
            }
            else
            {
                ShowStatusLabel($"重新加载成功: {filePath}");
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

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    // /select, 会打开文件夹并选中该文件
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                catch (Exception ex)
                {
                    ShowStatusError($"无法打开文件位置: {ex.Message}");
                }
            }
            else
            {
                ShowStatusError("文件路径无效或文件不存在。");
            }
        }

        // 查看程序集信息
        private void ContextMenuAssemblyInfo_Click(object sender, RoutedEventArgs e)
        {
            if (commandsTreeView.SelectedItem is TreeViewItem item && item.Tag is AddinItem addinItem)
            {
                var filePath = addinItem.AssemblyPath;
                if (!File.Exists(filePath))
                {
                    ShowStatusError($"程序集文件不存在: {filePath}");
                    return;
                }

                try
                {
                    // 注意：LoadFrom会锁定文件。对于更复杂的场景，可能需要使用不同的加载上下文。
                    var assembly = Assembly.LoadFrom(filePath);
                    var assemblyName = assembly.GetName();
                    var referencedAssemblies = assembly.GetReferencedAssemblies();

                    var info = new System.Text.StringBuilder();
                    info.AppendLine($"程序集: {assemblyName.Name}");
                    info.AppendLine($"版本: {assemblyName.Version}");
                    info.AppendLine($"完整名称: {assembly.FullName}");
                    info.AppendLine("\n--- 依赖项 ---");

                    foreach (var refAssembly in referencedAssemblies)
                    {
                        info.AppendLine(refAssembly.FullName);
                    }

                    // 这里我们用MessageBox来显示信息。
                    // 在实际项目中，您可能想创建一个新的窗口来更友好地展示这些信息。
                    MessageBox.Show(info.ToString(), "程序集信息", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowStatusError($"无法加载程序集信息: {ex.Message}");
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
            var treeView = sender as TreeView;
            if (treeView == null) return;

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
    }
}
