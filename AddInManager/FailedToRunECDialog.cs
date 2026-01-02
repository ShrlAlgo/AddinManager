using System.Windows;

using AddInManager.Properties;

namespace AddInManager
{
    public class FailedToRunECDialog
    {
        public static MessageBoxResult Show(string ecName)
        {
            var text = $"选中外部命令 [{ecName}] 返回 \"Result.Failed\",请检查测试脚本";
            return MessageBox.Show(text, Resources.AppName, MessageBoxButton.OK);
        }
    }
}
