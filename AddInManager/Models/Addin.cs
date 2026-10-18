using System;
using System.Collections.Generic;
using System.IO;

using AddInManager.Core;

namespace AddInManager.Models
{
    public class Addin : IAddinNode
    {
        public List<AddinItem> ItemList { get; set; }

        public string FilePath { get; set; }

        public bool Save { get; set; }

        public bool Hidden { get; set; }

        public bool IsExpanded { get; set; } = true;

        public DateTime? LoadedFileWriteTimeUtc { get; set; }

        // JSON 时间精度为毫秒，文件时间使用相同精度避免重启后误报更新。
        internal static DateTime? GetFileWriteTimeUtc(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                var time = File.GetLastWriteTimeUtc(filePath);
                return new DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
            }
            catch (System.IO.IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public Addin(string filePath)
        {
            ItemList = new List<AddinItem>();
            FilePath = filePath;
            Save = false;
        }

        public Addin(string filePath, List<AddinItem> itemList)
        {
            ItemList = itemList;
            FilePath = filePath;
            SortAddinItem();
            Save = false;
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
