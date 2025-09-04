using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AddInManager
{
    public class IniFile
    {
        public string FilePath { get; }

        public IniFile(string filePath)
        {
            FilePath = filePath;
            if (!File.Exists(FilePath))
            {
                FileUtils.CreateFile(FilePath);
                FileUtils.SetWriteable(FilePath);
            }
        }

        public void WriteSection(string iniSection)
        {
            WritePrivateProfileSection(iniSection, null, FilePath);
        }

        public void Write(string iniSection, string iniKey, object iniValue)
        {
            WritePrivateProfileString(iniSection, iniKey, iniValue.ToString(), FilePath);
        }

        public string ReadString(string iniSection, string iniKey)
        {
            var stringBuilder = new StringBuilder(255);
            GetPrivateProfileString(iniSection, iniKey, string.Empty, stringBuilder, 255, FilePath);
            return stringBuilder.ToString();
        }

        public int ReadInt(string iniSection, string iniKey)
        {
            return GetPrivateProfileInt(iniSection, iniKey, 0, FilePath);
        }

        [DllImport("kernel32.dll")]
        private static extern int WritePrivateProfileSection(string lpAppName, string lpString, string lpFileName);

        [DllImport("kernel32", CharSet = CharSet.Auto)]
        private static extern int WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileInt(string section, string key, int def, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Auto)]
        private static extern int GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder retVal, int size, string filePath);
    }
}
