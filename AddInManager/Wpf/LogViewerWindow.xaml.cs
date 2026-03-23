using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

using AddInManager.Debug;

using Microsoft.Win32;

namespace AddInManager.Wpf
{
    public partial class LogViewerWindow : Window
    {
        private readonly DebugLogger _logger = DebugLogger.Instance;

        public LogViewerWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshList();
            _logger.EntryAdded += OnEntryAdded;
            UpdateStatus();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _logger.EntryAdded -= OnEntryAdded;
        }

        private void OnEntryAdded(object sender, LogEntry entry)
        {
            // EntryAdded is raised on the logger's caller thread; marshal to UI thread.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnEntryAdded(sender, entry));
                return;
            }

            if (PassesFilter(entry))
            {
                logListView.Items.Add(entry);
                if (chkAutoScroll.IsChecked == true && logListView.Items.Count > 0)
                    logListView.ScrollIntoView(logListView.Items[logListView.Items.Count - 1]);
            }

            UpdateStatus();
        }

        private bool PassesFilter(LogEntry entry)
        {
            switch (entry.Level)
            {
                case LogLevel.Info:    return chkInfo.IsChecked == true;
                case LogLevel.Warning: return chkWarning.IsChecked == true;
                case LogLevel.Error:   return chkError.IsChecked == true;
                default:               return true;
            }
        }

        private void RefreshList()
        {
            logListView.Items.Clear();
            foreach (var entry in _logger.Entries.Where(PassesFilter))
                logListView.Items.Add(entry);

            if (chkAutoScroll.IsChecked == true && logListView.Items.Count > 0)
                logListView.ScrollIntoView(logListView.Items[logListView.Items.Count - 1]);
        }

        private void UpdateStatus()
        {
            var total    = _logger.Entries.Count;
            var errors   = _logger.Entries.Count(e => e.Level == LogLevel.Error);
            var warnings = _logger.Entries.Count(e => e.Level == LogLevel.Warning);
            statusText.Text = $"共 {total} 条记录  |  错误: {errors}  |  警告: {warnings}";
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            RefreshList();
            UpdateStatus();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _logger.Clear();
            logListView.Items.Clear();
            UpdateStatus();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "导出日志",
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"AddinManager_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                foreach (var entry in _logger.Entries)
                    sb.AppendLine(entry.ToString());
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"日志已导出至:\n{dlg.FileName}", "导出成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
