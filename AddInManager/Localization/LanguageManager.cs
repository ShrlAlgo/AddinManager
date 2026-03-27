using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace AddInManager.Localization
{
    /// <summary>
    /// Manages UI language selection and persistence.
    /// </summary>
    public static class LanguageManager
    {
        private const string DefaultCulture = "zh-CN";
        private const string AppFolderName = "AddinData";

        /// <summary>
        /// Set to true when the user requests a language change so callers can reopen the window.
        /// </summary>
        public static bool RestartRequested { get; set; }

        /// <summary>
        /// The currently active culture name (e.g. "zh-CN", "en-US", "ja-JP").
        /// </summary>
        public static string CurrentCultureName { get; private set; } = DefaultCulture;

        /// <summary>
        /// Loads the saved language setting and applies it to the current thread.
        /// Call this early in the application lifecycle (e.g. OnStartup).
        /// </summary>
        public static void ApplySavedLanguage()
        {
            try
            {
                var cultureName = LoadSavedCulture();
                ApplyCulture(cultureName);
            }
            catch (Exception)
            {
                // If reading settings fails, keep the default culture
            }
        }

        /// <summary>
        /// Changes the UI language, saves the setting, and signals that the window should be reopened.
        /// </summary>
        public static void SetLanguage(string cultureName)
        {
            ApplyCulture(cultureName);
            SaveCulture(cultureName);
            RestartRequested = true;
        }

        private static void ApplyCulture(string cultureName)
        {
            if (string.IsNullOrEmpty(cultureName))
                cultureName = DefaultCulture;

            CurrentCultureName = cultureName;

            try
            {
                var culture = new CultureInfo(cultureName);
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                Properties.Resources.Culture = culture;
            }
            catch (Exception)
            {
                // Invalid culture name; fall back to default and apply it to the thread
                CurrentCultureName = DefaultCulture;
                var defaultCulture = new CultureInfo(DefaultCulture);
                Thread.CurrentThread.CurrentUICulture = defaultCulture;
                Thread.CurrentThread.CurrentCulture = defaultCulture;
                Properties.Resources.Culture = defaultCulture;
            }
        }

        private static string GetSettingsFilePath()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var dataFolder = Path.Combine(dir, AppFolderName);
            return Path.Combine(dataFolder, "ui-settings.json");
        }

        private static string LoadSavedCulture()
        {
            var path = GetSettingsFilePath();
            if (!File.Exists(path))
                return DefaultCulture;

            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0)
                    return DefaultCulture;

                using (var ms = new MemoryStream(bytes))
                {
                    var ser = new DataContractJsonSerializer(typeof(UiSettings));
                    var settings = ser.ReadObject(ms) as UiSettings;
                    return string.IsNullOrEmpty(settings?.Language) ? DefaultCulture : settings.Language;
                }
            }
            catch (Exception)
            {
                return DefaultCulture;
            }
        }

        private static void SaveCulture(string cultureName)
        {
            try
            {
                var path = GetSettingsFilePath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var settings = new UiSettings { Language = cultureName };
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(UiSettings));
                    ser.WriteObject(ms, settings);
                    File.WriteAllBytes(path, ms.ToArray());
                }
            }
            catch (Exception)
            {
                // ignore save errors
            }
        }

        [DataContract]
        private class UiSettings
        {
            [DataMember]
            public string Language { get; set; }
        }
    }
}
