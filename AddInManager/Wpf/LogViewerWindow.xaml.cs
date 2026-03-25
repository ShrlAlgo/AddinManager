using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

using AddInManager.DebugTools;

using Microsoft.Win32;

namespace AddInManager.Wpf
{
    public partial class LogViewerWindow : Window
    {
        private static LogViewerWindow _instance;
        private readonly DebugLogger _logger = DebugLogger.Instance;

        public static void ShowSingleton()
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new LogViewerWindow
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                _instance.Show();
                return;
            }

            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }

            if (!_instance.IsVisible)
            {
                _instance.Show();
            }

            _instance.Activate();
            _instance.Focus();
        }

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
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }

        private void ScrollToBottom()
        {
            if (chkAutoScroll.IsChecked == true && logListView.Items.Count > 0)
                logListView.ScrollIntoView(logListView.Items[logListView.Items.Count - 1]);
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
                ScrollToBottom();
            }

            UpdateStatus();
        }

        private bool PassesFilter(LogEntry entry)
        {
            return PassesLevelFilter(entry) && PassesSourceFilter(entry);
        }

        private bool PassesLevelFilter(LogEntry entry)
        {
            switch (entry.Level)
            {
                case LogLevel.Info: return chkInfo.IsChecked == true;
                case LogLevel.Warning: return chkWarning.IsChecked == true;
                case LogLevel.Error: return chkError.IsChecked == true;
                default: return true;
            }
        }

        private bool PassesSourceFilter(LogEntry entry)
        {
            switch (entry.Source)
            {
                case "DebugLogger": return chkDebugLogger.IsChecked == true;
                case "System.Diagnostics": return chkSystemDiagnostics.IsChecked == true;
                default: return true;
            }
        }

        private void RefreshList()
        {
            logListView.Items.Clear();
            foreach (var entry in _logger.Entries.Where(PassesFilter))
                logListView.Items.Add(entry);
            ScrollToBottom();
        }

        private void UpdateStatus()
        {
            var total = _logger.Entries.Count;
            var errors = _logger.Entries.Count(e => e.Level == LogLevel.Error);
            var warnings = _logger.Entries.Count(e => e.Level == LogLevel.Warning);
            statusText.Text = string.Format(Properties.Resources.LogStatusFormat, total, errors, warnings);
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || logListView == null || statusText == null) return;
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
                Title = Properties.Resources.LogExportTitle,
                Filter = Properties.Resources.LogExportFilter,
                FileName = $"AddinManager_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                foreach (var entry in _logger.Entries)
                    sb.AppendLine(entry.ToString());
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show(string.Format(Properties.Resources.LogExportedTo, dlg.FileName),
                    Properties.Resources.LogExportSuccess,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.LogExportFailed, ex.Message),
                    Properties.Resources.ErrorTitle,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
