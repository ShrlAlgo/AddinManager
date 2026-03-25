using System;
using System.Windows;

namespace AddInManager.Wpf
{
    public partial class UnhandledExceptionDialog : Window
    {
        private readonly Exception _exception;

        public UnhandledExceptionDialog(Exception exception)
        {
            _exception = exception;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            exceptionTypeText.Text = _exception?.GetType().FullName ?? "Unknown";
            messageText.Text       = _exception?.Message ?? string.Empty;
            stackTraceText.Text    = _exception?.StackTrace ?? string.Empty;
        }

        private string FormatExceptionDetail()
        {
            return _exception == null
                ? "(no exception details)"
                : $"{_exception.GetType().FullName}: {_exception.Message}{Environment.NewLine}{_exception.StackTrace}";
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(FormatExceptionDetail(), true);
                MessageBox.Show(Properties.Resources.CopiedMessage, Properties.Resources.CopiedTitle,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                // Clipboard may be unavailable in some environments
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
