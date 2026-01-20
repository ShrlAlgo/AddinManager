using System.Collections.Generic;
using System.IO;

namespace AddInManager
{
    public class Addin : IAddinNode
    {
        public List<AddinItem> ItemList { get; set; }

        public string FilePath { get; set; }

        public bool Save { get; set; }

        public bool Hidden { get; set; }

        public Addin(string filePath)
        {
            ItemList = new List<AddinItem>();
            FilePath = filePath;
            Save = true;
        }

        public Addin(string filePath, List<AddinItem> itemList)
        {
            ItemList = itemList;
            FilePath = filePath;
            SortAddinItem();
            Save = true;
        }

        public void SortAddinItem()
        {
            ItemList.Sort(new AddinItemComparer());
        }

        public void RemoveItem(AddinItem item)
        {
            ItemList.Remove(item);
            if (ItemList.Count == 0)
            {
                AIM.Instance.AddinManager.RemoveAddin(this);
            }
        }

        public void SaveToLocalIni(InitFile file)
        {
            if (ItemList == null || ItemList.Count == 0)
            {
                return;
            }
            var addinType = ItemList[0].AddinType;
            if (addinType == AddinType.Command)
            {
                file.WriteSection("ExternalCommands");
                file.Write("ExternalCommands", "ECCount", 0);
                var num = 0;
                foreach (var addinItem in ItemList)
                {
                    if (addinItem.Save)
                    {
                        WriteExternalCommand(file, addinItem, ++num);
                    }
                }
                file.Write("ExternalCommands", "ECCount", num);
                return;
            }
            file.WriteSection("ExternalApplications");
            file.Write("ExternalApplications", "EACount", 0);
            var num2 = 0;
            foreach (var addinItem2 in ItemList)
            {
                WriteExternalApplication(file, addinItem2, ++num2);
            }
            file.Write("ExternalApplications", "EACount", num2);
        }

        private void WriteExternalCommand(InitFile file, AddinItem item, int number)
        {
            file.Write("ExternalCommands", $"ECName{number}", item.Name);
            file.Write("ExternalCommands", $"ECClassName{number}", item.FullClassName);
            file.Write("ExternalCommands", $"ECAssembly{number}", item.AssemblyName);
            file.Write("ExternalCommands", $"ECDescription{number}", item.Description);
        }

        private void WriteExternalApplication(InitFile file, AddinItem item, int number)
        {
            file.Write("ExternalApplications", $"EAClassName{number}", item.FullClassName);
            file.Write("ExternalApplications", $"EAAssembly{number}", item.AssemblyName);
        }

        public void SaveToLocalManifest()
        {
            if (ItemList == null || ItemList.Count == 0)
            {
                return;
            }
            var addinType = ItemList[0].AddinType;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(FilePath);
            var manifestFile = new ManifestFile($"{fileNameWithoutExtension}.addin");
            if (addinType == AddinType.Application)
            {
                manifestFile.Applications = ItemList;
            }
            else if (addinType == AddinType.Command)
            {
                manifestFile.Commands = ItemList;
            }
            manifestFile.Save();
        }
    }
}
