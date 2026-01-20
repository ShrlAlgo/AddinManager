using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
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

        public InitFile AimJsonFile { get; set; }

        public InitFile RevitIniFile { get; set; }

        private void GetIniFilePaths()
        {
            //var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var appFolder = Path.Combine(folderPath, Resources.AppFolder);
            var jsonFilePath = Path.Combine(appFolder, "AimInternal.json");
            AimJsonFile = new InitFile(jsonFilePath);
            try
            {
                var oldIniPath = Path.Combine(appFolder, "AimInternal.ini");
                if (File.Exists(oldIniPath) && !File.Exists(jsonFilePath))
                {
                    var oldIni = new InitFile(oldIniPath);
                    Commands.ReadItems(oldIni);
                    Applications.ReadItems(oldIni);

                    SaveToPersistentStore(jsonFilePath);
                    try
                    {
                        var backupPath = oldIniPath + ".bak";
                        File.Copy(oldIniPath, backupPath, true);
                        FileUtils.SetWriteable(oldIniPath);
                        File.Delete(oldIniPath);
                    }
                    catch (Exception)
                    {
                        // ignore backup errors
                    }
                }
            }
            catch (Exception)
            {
                // ignore migration errors
            }

            var currentProcess = Process.GetCurrentProcess();
            var fileName = currentProcess.MainModule.FileName;
            var revitIniFilePath = fileName.Replace(".exe", ".ini");
            RevitIniFile = new InitFile(revitIniFilePath);
        }

        public void ReadAddinsFromAimIni()
        {
            if (!LoadFromPersistentStore(AimJsonFile.FilePath))
            {
                Commands.ReadItems(AimJsonFile);
                Applications.ReadItems(AimJsonFile);
            }
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

        public void SaveToLocal(AddinType addinTypeToSave)
        {
            SaveToLocalManifest(addinTypeToSave);
        }

        public void SaveToAimIni()
        {
            // ensure file exists
            try
            {
                if (!File.Exists(AimJsonFile.FilePath))
                {
                    new FileInfo(AimJsonFile.FilePath).Directory?.Create();
                    FileUtils.CreateFile(AimJsonFile.FilePath);
                    FileUtils.SetWriteable(AimJsonFile.FilePath);
                }
            }
            catch (Exception)
            {
                // ignore
            }

            // save to persistent JSON store; if fails, fall back to legacy INI writer
            if (!SaveToPersistentStore(AimJsonFile.FilePath))
            {
                Commands.Save(AimJsonFile);
                Applications.Save(AimJsonFile);
            }
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
                addinFilePath = GetProperFilePath(currentAddinFolder, addinFileName, ".addin");
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

        private bool LoadFromPersistentStore(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
                using (var fs = File.OpenRead(filePath))
                {
                    var ser = new DataContractJsonSerializer(typeof(PersistentAddinStore));
                    var obj = ser.ReadObject(fs) as PersistentAddinStore;
                    if (obj == null) return false;


                    // reset internal dictionaries
                    Commands.AddinDict.Clear();
                    Applications.AddinDict.Clear();

                    foreach (var p in obj.Commands)
                    {
                        var items = new List<AddinItem>();
                        foreach (var pi in p.Items)
                        {
                            var ai = new AddinItem(pi.AssemblyPath ?? string.Empty, pi.ClientId == Guid.Empty ? Guid.NewGuid() : pi.ClientId, pi.FullClassName ?? string.Empty, AddinType.Command, pi.TransactionMode, pi.RegenerationMode, pi.JournalingMode)
                            {
                                Name = pi.Name,
                                Description = pi.Description,
                                VisibilityMode = pi.VisibilityMode,
                                Save = pi.Save,
                                Hidden = pi.Hidden
                            };
                            items.Add(ai);
                        }
                        var addin = new Addin(p.FilePath ?? string.Empty, items)
                        {
                            Save = p.Save,
                            Hidden = p.Hidden
                        };
                        Commands.AddAddIn(addin);
                    }

                    foreach (var p in obj.Applications)
                    {
                        var items = new List<AddinItem>();
                        foreach (var pi in p.Items)
                        {
                            var ai = new AddinItem(pi.AssemblyPath ?? string.Empty, pi.ClientId == Guid.Empty ? Guid.NewGuid() : pi.ClientId, pi.FullClassName ?? string.Empty, AddinType.Application, pi.TransactionMode, pi.RegenerationMode, pi.JournalingMode)
                            {
                                Name = pi.Name,
                                Description = pi.Description,
                                VisibilityMode = pi.VisibilityMode,
                                Save = pi.Save,
                                Hidden = pi.Hidden
                            };
                            items.Add(ai);
                        }
                        var addin = new Addin(p.FilePath ?? string.Empty, items)
                        {
                            Save = p.Save,
                            Hidden = p.Hidden
                        };
                        Applications.AddAddIn(addin);
                    }

                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool SaveToPersistentStore(string filePath)
        {
            try
            {
                var store = new PersistentAddinStore
                {
                    FormatVersion = 1,
                    RevitVersion = App.RevitVersion,
                    LastSaved = DateTime.Now
                };

                foreach (var kv in Commands.AddinDict)
                {
                    var a = kv.Value;
                    var p = new PersistentAddin
                    {
                        FilePath = a.FilePath,
                        Save = a.Save,
                        Hidden = a.Hidden
                    };
                    foreach (var ai in a.ItemList)
                    {
                        var pi = new PersistentAddinItem
                        {
                            AddinType = ai.AddinType,
                            AssemblyPath = ai.AssemblyPath,
                            AssemblyName = ai.AssemblyName,
                            ClientId = ai.ClientId,
                            FullClassName = ai.FullClassName,
                            Name = ai.Name,
                            Description = ai.Description,
                            VisibilityMode = ai.VisibilityMode,
                            Save = ai.Save,
                            Hidden = ai.Hidden,
                            TransactionMode = ai.TransactionMode,
                            RegenerationMode = ai.RegenerationMode,
                            JournalingMode = ai.JournalingMode
                        };
                        p.Items.Add(pi);
                    }
                    store.Commands.Add(p);
                }

                foreach (var kv in Applications.AddinDict)
                {
                    var a = kv.Value;
                    var p = new PersistentAddin
                    {
                        FilePath = a.FilePath,
                        Save = a.Save,
                        Hidden = a.Hidden
                    };
                    foreach (var ai in a.ItemList)
                    {
                        var pi = new PersistentAddinItem
                        {
                            AddinType = ai.AddinType,
                            AssemblyPath = ai.AssemblyPath,
                            AssemblyName = ai.AssemblyName,
                            ClientId = ai.ClientId,
                            FullClassName = ai.FullClassName,
                            Name = ai.Name,
                            Description = ai.Description,
                            VisibilityMode = ai.VisibilityMode,
                            Save = ai.Save,
                            Hidden = ai.Hidden,
                            TransactionMode = ai.TransactionMode,
                            RegenerationMode = ai.RegenerationMode,
                            JournalingMode = ai.JournalingMode
                        };
                        p.Items.Add(pi);
                    }
                    store.Applications.Add(p);
                }

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

                using (var fs = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var ser = new DataContractJsonSerializer(typeof(PersistentAddinStore));
                    ser.WriteObject(fs, store);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
