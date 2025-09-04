
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

using AddInManager.Properties;

namespace AddInManager
{
    public class AddinManager
    {
        public AddinsApplication Applications { get; }

        public int AppCount => Applications.Count;

        public AddinsCommand Commands { get; }

        public int CmdCount => Commands.Count;

        public AddinManager()
        {
            Commands = new AddinsCommand();
            Applications = new AddinsApplication();
            GetIniFilePaths();
            ReadAddinsFromAimIni();
        }

        public IniFile AimIniFile { get; set; }

        public IniFile RevitIniFile { get; set; }

        private void GetIniFilePaths()
        {
            var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var text = Path.Combine(folderPath, Settings.Default.AppFolder);
            var text2 = Path.Combine(text, "AimInternal.ini");
            AimIniFile = new IniFile(text2);
            var currentProcess = Process.GetCurrentProcess();
            var fileName = currentProcess.MainModule.FileName;
            var text3 = fileName.Replace(".exe", ".ini");
            RevitIniFile = new IniFile(text3);
        }

        public void ReadAddinsFromAimIni()
        {
            Commands.ReadItems(AimIniFile);
            Applications.ReadItems(AimIniFile);
        }

        public void RemoveAddin(Addin addin)
        {
            if (!Commands.RemoveAddIn(addin))
            {
                Applications.RemoveAddIn(addin);
            }
        }

        public AddinType LoadAddin(string filePath)
        {
            var addinType = AddinType.Invalid;
            if (!File.Exists(filePath))
            {
                return addinType;
            }
            Path.GetFileName(filePath);
            var assemLoader = new AssemLoader();
            List<AddinItem> list = null;
            List<AddinItem> list2 = null;
            try
            {
                assemLoader.HookAssemblyResolve();
                var assembly = assemLoader.LoadAddinsToTempFolder(filePath, true);
                if (null == assembly)
                {
                    return addinType;
                }
                list = Commands.LoadItems(assembly, StaticUtil.m_ecFullName, filePath, AddinType.Command);
                list2 = Applications.LoadItems(assembly, StaticUtil.m_eaFullName, filePath, AddinType.Application);
            }
            catch (Exception)
            {
            }
            finally
            {
                assemLoader.UnhookAssemblyResolve();
            }
            if (list != null && list.Count > 0)
            {
                var addin = new Addin(filePath, list);
                Commands.AddAddIn(addin);
                addinType |= AddinType.Command;
            }
            if (list2 != null && list2.Count > 0)
            {
                var addin2 = new Addin(filePath, list2);
                Applications.AddAddIn(addin2);
                addinType |= AddinType.Application;
            }
            return addinType;
        }

        public void SaveToRevitIni()
        {
            if (!File.Exists(RevitIniFile.FilePath))
            {
                throw new System.IO.FileNotFoundException(
                    $"can't find the revit.ini file from: {RevitIniFile.FilePath}",
    RevitIniFile.FilePath
);
            }
            Commands.Save(RevitIniFile);
            Applications.Save(RevitIniFile);
        }

        public void SaveToLocal()
        {
            SaveToLocalManifest();
        }

        public void SaveToLocalRevitIni()
        {
            foreach (var keyValuePair in Commands.AddinDict)
            {
                var key = keyValuePair.Key;
                var value = keyValuePair.Value;
                var directoryName = Path.GetDirectoryName(value.FilePath);
                var iniFile = new IniFile(Path.Combine(directoryName, "revit.ini"));
                value.SaveToLocalIni(iniFile);
                if (Applications.AddinDict.ContainsKey(key))
                {
                    var addin = Applications.AddinDict[key];
                    addin.SaveToLocalIni(iniFile);
                }
            }
        }

        public void SaveToAimIni()
        {
            if (!File.Exists(AimIniFile.FilePath))
            {
                new FileInfo(AimIniFile.FilePath).Create();
                FileUtils.SetWriteable(AimIniFile.FilePath);
            }
            Commands.Save(AimIniFile);
            Applications.Save(AimIniFile);
        }

        public bool HasItemsToSave()
        {
            foreach (var addin in Commands.AddinDict.Values)
            {
                if (addin.Save)
                {
                    return true;
                }
            }
            foreach (var addin2 in Applications.AddinDict.Values)
            {
                if (addin2.Save)
                {
                    return true;
                }
            }
            return false;
        }

        public string SaveToAllUserManifest()
        {
            var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var currentAddinFolder = Path.Combine(folderPath, $"Autodesk\\Revit\\Addins\\{App.RevitVersion}");
            var manifestFile = new ManifestFile(false);
            var numCmdAddins = 0;
            Addin savedCmdAddin = null;
            foreach (var cmdAddin in Commands.AddinDict.Values)
            {
                if (cmdAddin.Save)
                {
                    numCmdAddins++;
                    savedCmdAddin = cmdAddin;
                }
                foreach (var addinItem in cmdAddin.ItemList)
                {
                    if (addinItem.Save)
                    {
                        manifestFile.Commands.Add(addinItem);
                    }
                }
            }
            var numAppAddins = 0;
            Addin savedAppAddin = null;
            foreach (var appAddin in Applications.AddinDict.Values)
            {
                if (appAddin.Save)
                {
                    numCmdAddins++;
                    savedAppAddin = appAddin;
                }
                foreach (var appAddinItem in appAddin.ItemList)
                {
                    if (appAddinItem.Save)
                    {
                        manifestFile.Applications.Add(appAddinItem);
                        numAppAddins++;
                        savedAppAddin = appAddin;
                    }
                }
            }
            var addinFileName = string.Empty;
            string addinFilePath;
            if (numCmdAddins <= 1 && numAppAddins <= 1 && numCmdAddins + numAppAddins > 0)
            {
                if (savedCmdAddin != null)
                {
                    if (savedAppAddin == null || savedCmdAddin.FilePath.Equals(savedAppAddin.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        addinFileName = Path.GetFileNameWithoutExtension(savedCmdAddin.FilePath);
                    }
                }
                else if (savedAppAddin != null && savedCmdAddin == null)
                {
                    addinFileName = Path.GetFileNameWithoutExtension(savedAppAddin.FilePath);
                }
                if (string.IsNullOrEmpty(addinFileName))
                {
                    return string.Empty;
                }
                addinFilePath = GetProperFilePath(currentAddinFolder, addinFileName, ".addin");
            }
            else
            {
                addinFilePath = GetProperFilePath(currentAddinFolder, "ExternalTool", ".addin");
            }
            manifestFile.SaveAs(addinFilePath);
            return addinFilePath;
        }

        public void SaveToLocalManifest()
        {
            var dictionary = new Dictionary<string, Addin>();
            var dictionary2 = new Dictionary<string, Addin>();
            foreach (var cmdKeyValuePair in Commands.AddinDict)
            {
                var key = cmdKeyValuePair.Key;
                var value = cmdKeyValuePair.Value;
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(value.FilePath);
                var directoryName = Path.GetDirectoryName(value.FilePath);
                var text = Path.Combine(directoryName, $"{fileNameWithoutExtension}.addin");
                var manifestFile = new ManifestFile(true);
                foreach (var addinItem in value.ItemList)
                {
                    if (addinItem.Save)
                    {
                        manifestFile.Commands.Add(addinItem);
                    }
                }
                if (Applications.AddinDict.ContainsKey(key))
                {
                    var addin = Applications.AddinDict[key];
                    foreach (var addinItem2 in addin.ItemList)
                    {
                        if (addinItem2.Save)
                        {
                            manifestFile.Applications.Add(addinItem2);
                        }
                    }
                    dictionary.Add(key, Applications.AddinDict[key]);
                }
                manifestFile.SaveAs(text);
            }
            foreach (var appKeyValuePair in Applications.AddinDict)
            {
                var key2 = appKeyValuePair.Key;
                var value2 = appKeyValuePair.Value;
                if (!dictionary.ContainsKey(key2))
                {
                    var fileNameWithoutExtension2 = Path.GetFileNameWithoutExtension(value2.FilePath);
                    var directoryName2 = Path.GetDirectoryName(value2.FilePath);
                    var text2 = Path.Combine(directoryName2, $"{fileNameWithoutExtension2}.addin");
                    var manifestFile2 = new ManifestFile(true);
                    foreach (var addinItem3 in value2.ItemList)
                    {
                        if (addinItem3.Save)
                        {
                            manifestFile2.Applications.Add(addinItem3);
                        }
                    }
                    if (Commands.AddinDict.ContainsKey(key2))
                    {
                        var addin2 = Commands.AddinDict[key2];
                        foreach (var addinItem4 in addin2.ItemList)
                        {
                            if (addinItem4.Save)
                            {
                                manifestFile2.Commands.Add(addinItem4);
                            }
                        }
                        dictionary2.Add(key2, Commands.AddinDict[key2]);
                    }
                    manifestFile2.SaveAs(text2);
                }
            }
        }

        private static string GetProperFilePath(string folder, string fileNameWithoutExt, string ext)
        {
            var filePath = string.Empty;
            var fileIndex = -1;
            do
            {
                fileIndex++;
                var text2 = ((fileIndex <= 0) ? (fileNameWithoutExt + ext) : (fileNameWithoutExt + fileIndex + ext));
                filePath = Path.Combine(folder, text2);
            }
            while (File.Exists(filePath));
            return filePath;
        }
    }
}
