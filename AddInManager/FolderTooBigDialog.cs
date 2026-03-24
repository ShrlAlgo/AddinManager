using AddInManager.Properties;
using System.Text;
using System.Windows;

namespace AddInManager
{
    public abstract class FolderTooBigDialog
    {
        public static MessageBoxResult Show(string folderPath, long sizeInMB)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(string.Format(Resources.FolderTooBigLine1, folderPath));
            stringBuilder.AppendLine(string.Format(Resources.FolderTooBigLine2, sizeInMB));
            stringBuilder.AppendLine(Resources.FolderTooBigLine3);
            stringBuilder.AppendLine(Resources.FolderTooBigLine4);
            stringBuilder.AppendLine(Resources.FolderTooBigLine5);
            stringBuilder.AppendLine(Resources.FolderTooBigLine6);
            var text = stringBuilder.ToString();
            return MessageBox.Show(text, Resources.AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Information, MessageBoxResult.Yes);
        }
    }
}
