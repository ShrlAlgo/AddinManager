using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;

namespace AddInManager
{
    public class IniFile
    {
        public string FilePath { get; }

        private readonly bool m_isJson;
        private Dictionary<string, Dictionary<string, string>> m_jsonData;

        public IniFile(string filePath)
        {
            FilePath = filePath;
            m_isJson = string.Equals(Path.GetExtension(FilePath), ".json", StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(FilePath))
            {
                FileUtils.CreateFile(FilePath);
                FileUtils.SetWriteable(FilePath);
                if (m_isJson)
                {
                    m_jsonData = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                    SaveJson();
                }
            }

            if (m_isJson)
            {
                LoadJson();
            }
        }

        public void WriteSection(string iniSection)
        {
            if (m_isJson)
            {
                if (m_jsonData == null)
                {
                    m_jsonData = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                }
                m_jsonData[iniSection] = new Dictionary<string, string>(StringComparer.Ordinal);
                SaveJson();
                return;
            }
            WritePrivateProfileSection(iniSection, null, FilePath);
        }

        public void Write(string iniSection, string iniKey, object iniValue)
        {
            if (m_isJson)
            {
                if (m_jsonData == null)
                {
                    m_jsonData = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                }
                if (!m_jsonData.ContainsKey(iniSection))
                {
                    m_jsonData[iniSection] = new Dictionary<string, string>(StringComparer.Ordinal);
                }
                m_jsonData[iniSection][iniKey] = iniValue?.ToString() ?? string.Empty;
                SaveJson();
                return;
            }
            WritePrivateProfileString(iniSection, iniKey, iniValue.ToString(), FilePath);
        }

        public string ReadString(string iniSection, string iniKey)
        {
            if (m_isJson)
            {
                if (m_jsonData != null && m_jsonData.TryGetValue(iniSection, out var section))
                {
                    if (section != null && section.TryGetValue(iniKey, out var val))
                    {
                        return val;
                    }
                }
                return string.Empty;
            }

            var stringBuilder = new StringBuilder(255);
            GetPrivateProfileString(iniSection, iniKey, string.Empty, stringBuilder, 255, FilePath);
            return stringBuilder.ToString();
        }

        public int ReadInt(string iniSection, string iniKey)
        {
            if (m_isJson)
            {
                var s = ReadString(iniSection, iniKey);
                if (int.TryParse(s, out var v)) return v;
                return 0;
            }
            return GetPrivateProfileInt(iniSection, iniKey, 0, FilePath);
        }

        private void LoadJson()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    m_jsonData = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                    return;
                }
                var bytes = File.ReadAllBytes(FilePath);
                if (bytes == null || bytes.Length == 0)
                {
                    m_jsonData = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                    return;
                }
                using (var ms = new MemoryStream(bytes))
                {
                    var ser = new DataContractJsonSerializer(typeof(Dictionary<string, Dictionary<string, string>>));
                    var obj = ser.ReadObject(ms) as Dictionary<string, Dictionary<string, string>>;
                    m_jsonData = obj ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                }
            }
            catch (Exception)
            {
                m_jsonData = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            }
        }

        private void SaveJson()
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(Dictionary<string, Dictionary<string, string>>));
                    ser.WriteObject(ms, m_jsonData ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal));
                    var data = ms.ToArray();
                    File.WriteAllBytes(FilePath, data);
                }
            }
            catch (Exception)
            {
                // ignore
            }
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
