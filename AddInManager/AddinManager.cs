using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            var appFolder = Path.Combine(folderPath, Resources.AppFolder);
            var iniFilePath = Path.Combine(appFolder, "AimInternal.ini");
            AimIniFile = new IniFile(iniFilePath);
            var currentProcess = Process.GetCurrentProcess();
            var fileName = currentProcess.MainModule.FileName;
            var revitIniFilePath = fileName.Replace(".exe", ".ini");
            RevitIniFile = new IniFile(revitIniFilePath);
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
            List<AddinItem> cmdItems = null;
            List<AddinItem> appItems = null;
            try
            {
                assemLoader.HookAssemblyResolve();
                var assembly = assemLoader.LoadAddinsToTempFolder(filePath, true);
                if (null == assembly)
                {
                    return addinType;
                }
                cmdItems = Commands.LoadItems(assembly, StaticUtil.m_ecFullName, filePath, AddinType.Command);
                appItems = Applications.LoadItems(assembly, StaticUtil.m_eaFullName, filePath, AddinType.Application);
            }
            catch (Exception)
            {
            }
            finally
            {
                assemLoader.UnhookAssemblyResolve();
            }
            if (cmdItems != null && cmdItems.Count > 0)
            {
                var cmdAddin = new Addin(filePath, cmdItems);
                Commands.AddAddIn(cmdAddin);
                addinType |= AddinType.Command;
            }
            if (appItems != null && appItems.Count > 0)
            {
                var appAddin = new Addin(filePath, appItems);
                Applications.AddAddIn(appAddin);
                addinType |= AddinType.Application;
            }
            return addinType;
        }

        public void SaveToRevitIni()
        {
            if (!File.Exists(RevitIniFile.FilePath))
            {
                throw new System.IO.FileNotFoundException($"路径{RevitIniFile.FilePath}中未找到revit.ini: ",
    RevitIniFile.FilePath
);
            }
            Commands.Save(RevitIniFile);
            Applications.Save(RevitIniFile);
        }

        public void SaveToLocal(AddinType addinTypeToSave)
        {
            SaveToLocalManifest(addinTypeToSave);
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

        public string SaveToAllUserManifest(AddinType addinTypeToSave)
        {
            var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var currentAddinFolder = Path.Combine(folderPath, $"Autodesk\\Revit\\Addins\\{App.RevitVersion}");
            var manifestFile = new ManifestFile(false);
            var numCmdAddins = 0;
            Addin savedCmdAddin = null;
            if (addinTypeToSave == AddinType.Command)
            {
                foreach (var cmdAddin in Commands.AddinDict.Values.Where(a => a.Save))
                {
                    numCmdAddins++;
                    savedCmdAddin = cmdAddin;
                    foreach (var addinItem in cmdAddin.ItemList.Where(i => i.Save))
                    {
                        manifestFile.Commands.Add(addinItem);
                    }
                }
            }

            var numAppAddins = 0;
            Addin savedAppAddin = null;
            if (addinTypeToSave == AddinType.Application)
            {
                foreach (var appAddin in Applications.AddinDict.Values.Where(a => a.Save))
                {
                    numAppAddins++;
                    savedAppAddin = appAddin;
                    foreach (var appAddinItem in appAddin.ItemList.Where(i => i.Save))
                    {
                        manifestFile.Applications.Add(appAddinItem);
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

        public void SaveToLocalManifest(AddinType addinTypeToSave)
        {
            if (addinTypeToSave == AddinType.Command)
            {
                foreach (var cmdKeyValuePair in Commands.AddinDict.Where(kv => kv.Value.Save))
                {
                    var value = cmdKeyValuePair.Value;
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(value.FilePath);
                    var directoryName = Path.GetDirectoryName(value.FilePath);
                    var text = Path.Combine(directoryName, $"{fileNameWithoutExtension}.addin");
                    var manifestFile = new ManifestFile(true);
                    foreach (var addinItem in value.ItemList.Where(i => i.Save))
                    {
                        manifestFile.Commands.Add(addinItem);
                    }
                    manifestFile.SaveAs(text);
                }
            }

            if (addinTypeToSave == AddinType.Application)
            {
                foreach (var appKeyValuePair in Applications.AddinDict.Where(kv => kv.Value.Save))
                {
                    var value2 = appKeyValuePair.Value;
                    var fileNameWithoutExtension2 = Path.GetFileNameWithoutExtension(value2.FilePath);
                    var directoryName2 = Path.GetDirectoryName(value2.FilePath);
                    var text2 = Path.Combine(directoryName2, $"{fileNameWithoutExtension2}.addin");
                    var manifestFile2 = new ManifestFile(true);
                    foreach (var addinItem3 in value2.ItemList.Where(i => i.Save))
                    {
                        manifestFile2.Applications.Add(addinItem3);
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
