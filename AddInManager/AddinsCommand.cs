namespace AddInManager
{
    public class AddinsCommand : Addins
    {
        public void ReadItems(InitFile file)
        {
            var num = file.ReadInt("ExternalCommands", "ECCount");
            var i = 1;
            while (i <= num)
            {
                ReadExternalCommand(file, i++);
            }
            SortAddin();
        }

        private bool ReadExternalCommand(InitFile file, int nodeNumber)
        {
            var text = file.ReadString("ExternalCommands", $"ECName{nodeNumber}");
            var text2 = file.ReadString("ExternalCommands", $"ECAssembly{nodeNumber}");
            var text3 = file.ReadString("ExternalCommands", $"ECClassName{nodeNumber}");
            var text4 = file.ReadString("ExternalCommands", $"ECDescription{nodeNumber}");
            if (string.IsNullOrEmpty(text3) || string.IsNullOrEmpty(text2))
            {
                return false;
            }
            AddItem(new AddinItem(AddinType.Command)
            {
                Name = text,
                AssemblyPath = text2,
                FullClassName = text3,
                Description = text4
            });
            return true;
        }

        public void Save(InitFile file)
        {
            file.WriteSection("ExternalCommands");
            file.Write("ExternalCommands", "ECCount", m_maxCount);
            var num = 0;
            foreach (var addin in m_addinDict.Values)
            {
                foreach (var addinItem in addin.ItemList)
                {
                    if (num >= m_maxCount)
                    {
                        break;
                    }
                    if (addinItem.Save)
                    {
                        WriteExternalCommand(file, addinItem, ++num);
                    }
                }
            }
            file.Write("ExternalCommands", "ECCount", num);
        }

        private bool WriteExternalCommand(InitFile file, AddinItem item, int number)
        {
            file.Write("ExternalCommands", $"ECName{number}", item.Name);
            file.Write("ExternalCommands", $"ECClassName{number}", item.FullClassName);
            file.Write("ExternalCommands", $"ECAssembly{number}", item.AssemblyPath);
            file.Write("ExternalCommands", $"ECDescription{number}", item.Description);
            return true;
        }
    }
}
