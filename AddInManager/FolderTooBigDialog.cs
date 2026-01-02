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
            stringBuilder.AppendLine($"文件夹[{folderPath}]");
            stringBuilder.AppendLine($"有{sizeInMB}MB大小");
            stringBuilder.AppendLine("AddinManager尝试复制所有文件到临时文件夹");
            stringBuilder.AppendLine("选择[是(Y)]复制所有文件到临时文件夹");
            stringBuilder.AppendLine("选择[否(N)]仅复制测试DLL文件到临时文件夹");
            stringBuilder.AppendLine("选择[取消]取消操作");
            var text = stringBuilder.ToString();
            return MessageBox.Show(text, Resources.AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Information, MessageBoxResult.Yes);
        }
    }
}
