using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;

using AddInManager.DebugTools;
using AddInManager.Models;
using AddInManager.Properties;

namespace AddInManager.Persistence
{
    public class AddinManager
    {
        private readonly string m_persistentStoreFilePath;

        public AddinsApplication Applications { get; }

        public int AppCount => Applications.Count;

        public AddinsCommand Commands { get; }

        public int CmdCount => Commands.Count;

        public bool ReuseUnchangedAssemblies { get; set; }

        public AddinManager()
        {
            Commands = new AddinsCommand();
            Applications = new AddinsApplication();
            m_persistentStoreFilePath = GetPersistentStoreFilePath();
            ReadAddinsFromPersistentStore();
        }

        private static string GetPersistentStoreFilePath()
        {
            var folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var appFolder = Path.Combine(folderPath, Resources.AppFolder);
            return Path.Combine(appFolder, "AimInternal.json");
        }

        public void ReadAddinsFromPersistentStore()
        {
            DebugLogger.Instance.Info($"Read addins from {m_persistentStoreFilePath}");
            LoadFromPersistentStore(m_persistentStoreFilePath);
            DebugLogger.Instance.Info($"Read addins completed, Commands: {CmdCount}, Applications: {AppCount}");
        }

        public void RemoveAddin(Addin addin)
        {
            AssemLoader.InvalidateExecutionCache(addin.FilePath);
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
                DebugLogger.Instance.Warning($"LoadAddin: file not found: {filePath}");
                return addinType;
            }
            DebugLogger.Instance.Info($"LoadAddin: loading {filePath}");
            var loadedFileWriteTimeUtc = Addin.GetFileWriteTimeUtc(filePath);
            var assemLoader = new AssemLoader();
            List<AddinItem> cmdItems = null;
            List<AddinItem> appItems = null;
            try
            {
                assemLoader.HookAssemblyResolve();
                var assembly = assemLoader.LoadAddinsToTempFolder(filePath, true);
                if (null == assembly)
                {
                    DebugLogger.Instance.Error($"LoadAddin: failed to load assembly: {filePath}");
                    return addinType;
                }
                cmdItems = Commands.LoadItems(assembly, StaticUtil.m_ecFullName, filePath, AddinType.Command);
                appItems = Applications.LoadItems(assembly, StaticUtil.m_eaFullName, filePath, AddinType.Application);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.Error(ex, $"LoadAddin: {filePath}");
            }
            finally
            {
                assemLoader.UnhookAssemblyResolve();
            }
            if (cmdItems != null && cmdItems.Count > 0)
            {
                var cmdAddin = new Addin(filePath, cmdItems)
                {
                    LoadedFileWriteTimeUtc = loadedFileWriteTimeUtc,
                    IsExpanded = Commands.AddinDict.TryGetValue(Path.GetFileName(filePath), out var previousCmd) ? previousCmd.IsExpanded : true
                };
                // 重载保留同一路径、同一命令的运行记录。
                if (previousCmd != null && string.Equals(previousCmd.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in cmdAddin.ItemList)
                    {
                        item.LastRunUtc = previousCmd.ItemList.Find(previous => previous.FullClassName == item.FullClassName)?.LastRunUtc;
                    }
                }
                Commands.AddAddIn(cmdAddin);
                addinType |= AddinType.Command;
                DebugLogger.Instance.Info($"LoadAddin: found {cmdItems.Count} commands in {System.IO.Path.GetFileName(filePath)}");
            }
            if (appItems != null && appItems.Count > 0)
            {
                var appAddin = new Addin(filePath, appItems)
                {
                    LoadedFileWriteTimeUtc = loadedFileWriteTimeUtc,
                    IsExpanded = Applications.AddinDict.TryGetValue(Path.GetFileName(filePath), out var previousApp) ? previousApp.IsExpanded : true
                };
                Applications.AddAddIn(appAddin);
                addinType |= AddinType.Application;
                DebugLogger.Instance.Info($"LoadAddin: found {appItems.Count} applications in {System.IO.Path.GetFileName(filePath)}");
            }
            return addinType;
        }

        public void SaveToLocal(AddinType addinTypeToSave, string filePath)
        {
            SaveToLocalManifest(addinTypeToSave, filePath);
        }

        public void SaveToAimIni()
        {
            SaveToPersistentStore(m_persistentStoreFilePath);
        }

        // 从当前列表查找命令，兼容 Faceless 仍持有重载前对象的情况。
        internal void RecordCommandRun(string filePath, string fullClassName)
        {
            if (!Commands.AddinDict.TryGetValue(Path.GetFileName(filePath), out var addin) ||
                !string.Equals(addin.FilePath, filePath, StringComparison.OrdinalIgnoreCase)) return;

            var item = addin.ItemList.Find(command => command.FullClassName == fullClassName);
            if (item == null) return;
            item.LastRunUtc = DateTime.UtcNow;
            SaveToAimIni();
        }

        public bool HasItemsToSave(AddinType addinTypeToSave)
        {
            // 仅检查当前页签实际勾选的子项，避免其他页签的选择触发空文件保存。
            var addins = addinTypeToSave == AddinType.Command ? Commands.AddinDict.Values : Applications.AddinDict.Values;
            return addins.Any(a => a.Save && a.ItemList.Any(i => i.Save));
        }

        public string SaveToAllUserManifest(AddinType addinTypeToSave)
        {
            if (!HasItemsToSave(addinTypeToSave))
            {
                return string.Empty;
            }

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
                // 多个程序集合并保存时使用固定名称，并沿用现有的重名递增规则。
                addinFileName = "CombinedAddins";
                addinFilePath = GetProperFilePath(currentAddinFolder, addinFileName, ".addin");
            }

            manifestFile.SaveAs(addinFilePath);
            return addinFilePath;
        }

        public void SaveToLocalManifest(AddinType addinTypeToSave, string filePath)
        {
            if (addinTypeToSave != AddinType.Command && addinTypeToSave != AddinType.Application) return;

            // 用户可另选目录；合并选中项并写入 DLL 完整路径，避免清单移位后无法加载。
            var manifestFile = new ManifestFile(false);
            var addins = addinTypeToSave == AddinType.Command ? Commands.AddinDict.Values : Applications.AddinDict.Values;
            var items = addinTypeToSave == AddinType.Command ? manifestFile.Commands : manifestFile.Applications;
            foreach (var addin in addins.Where(a => a.Save))
            {
                items.AddRange(addin.ItemList.Where(i => i.Save));
            }
            if (items.Count > 0) manifestFile.SaveAs(filePath);
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

                    ReuseUnchangedAssemblies = obj.ReuseUnchangedAssemblies;


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
                                Hidden = pi.Hidden,
                                LastRunUtc = pi.LastRunUtc
                            };
                            items.Add(ai);
                        }
                        var addin = new Addin(p.FilePath ?? string.Empty, items)
                        {
                            Save = p.Save,
                            Hidden = p.Hidden,
                            IsExpanded = p.IsExpanded ?? true,
                            LoadedFileWriteTimeUtc = p.LoadedFileWriteTimeUtc
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
                                Hidden = pi.Hidden,
                                LastRunUtc = pi.LastRunUtc
                            };
                            items.Add(ai);
                        }
                        var addin = new Addin(p.FilePath ?? string.Empty, items)
                        {
                            Save = p.Save,
                            Hidden = p.Hidden,
                            IsExpanded = p.IsExpanded ?? true,
                            LoadedFileWriteTimeUtc = p.LoadedFileWriteTimeUtc
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
                    ReuseUnchangedAssemblies = ReuseUnchangedAssemblies,
                    LastSaved = DateTime.Now
                };

                foreach (var kv in Commands.AddinDict)
                {
                    var a = kv.Value;
                    var p = new PersistentAddin
                    {
                        FilePath = a.FilePath,
                        Save = a.Save,
                        Hidden = a.Hidden,
                        IsExpanded = a.IsExpanded,
                        LoadedFileWriteTimeUtc = a.LoadedFileWriteTimeUtc
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
                            LastRunUtc = ai.LastRunUtc,
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
                        Hidden = a.Hidden,
                        IsExpanded = a.IsExpanded,
                        LoadedFileWriteTimeUtc = a.LoadedFileWriteTimeUtc
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
                            LastRunUtc = ai.LastRunUtc,
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
