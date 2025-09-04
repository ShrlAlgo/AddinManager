using Microsoft.Win32;

using System;
using System.IO;
using System.Text;
using System.Windows;

namespace AddInManager.Wpf
{
    public partial class AssemblySelectorWindow : Window
    {
        private readonly string m_assemName;
        private bool m_found;
        public string ResultPath { get; private set; }

        public AssemblySelectorWindow(string assemName)
        {
            InitializeComponent();
            m_assemName = assemName;
            assemNameTextBox.Text = assemName;
            Closing += AssemblySelectorWindow_Closing;
        }

        private void AssemblySelectorWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!m_found)
            {
                ShowWarning();
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Assembly files (*.dll;*.exe,*.mcl)|*.dll;*.exe;*.mcl|All files|*.*||"
            };

            var assemblyBaseName = m_assemName.Substring(0, m_assemName.IndexOf(','));
            openFileDialog.FileName = $"{assemblyBaseName}.*";

            if (openFileDialog.ShowDialog() != true)
            {
                ShowWarning();
                return;
            }

            assemPathTextBox.Text = openFileDialog.FileName;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(assemPathTextBox.Text))
            {
                ResultPath = assemPathTextBox.Text;
                m_found = true;
                DialogResult = true;
            }
            else
            {
                ShowWarning();
            }
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowWarning()
        {
            var text = new StringBuilder("The dependent assembly can't be loaded: \"")
                .Append(m_assemName)
                .Append("\".")
                .ToString();
            MessageBox.Show(text, "Add-in Manager Internal", MessageBoxButton.OK, MessageBoxImage.Exclamation);
        }
    }
}