using System;
using System.IO;

using Autodesk.Revit.Attributes;

namespace AddInManager
{
    public class AddinItem : IAddinNode
    {
        public AddinItem(AddinType type)
        {
            AddinType = type;
            MClientId = Guid.NewGuid();
            ClientIdString = MClientId.ToString();
            MAssemblyPath = string.Empty;
            AssemblyName = string.Empty;
            FullClassName = string.Empty;
            _mName = string.Empty;
            Save = true;
            VisibilityMode = VisibilityMode.AlwaysVisible;
        }

        public AddinItem(string assemblyPath, Guid clientId, string fullClassName, AddinType type, TransactionMode? transactionMode, RegenerationOption? regenerationOption, JournalingMode? journalingMode)
        {
            TransactionMode = transactionMode;
            RegenerationMode = regenerationOption;
            JournalingMode = journalingMode;
            AddinType = type;
            MAssemblyPath = assemblyPath;
            AssemblyName = Path.GetFileName(MAssemblyPath);
            MClientId = clientId;
            ClientIdString = clientId.ToString();
            FullClassName = fullClassName;
            var num = fullClassName.LastIndexOf(".");
            _mName = fullClassName.Substring(num + 1);
            Save = true;
            VisibilityMode = VisibilityMode.AlwaysVisible;
        }

        public void SaveToManifest()
        {
            var manifestFile = new ManifestFile($"{_mName}.addin");
            if (AddinType == AddinType.Application)
            {
                manifestFile.Applications.Add(this);
            }
            else if (AddinType == AddinType.Command)
            {
                manifestFile.Commands.Add(this);
            }
            manifestFile.Save();
        }

        public AddinType AddinType { get; set; }

        public string AssemblyPath
        {
            get => MAssemblyPath;
            set
            {
                MAssemblyPath = value;
                AssemblyName = Path.GetFileName(MAssemblyPath);
            }
        }

        public string AssemblyName { get; set; }

        public Guid ClientId
        {
            get => MClientId;
            set
            {
                MClientId = value;
                ClientIdString = MClientId.ToString();
            }
        }

        protected internal string ClientIdString { get; set; }

        public string FullClassName { get; set; }

        public string Name
        {
            get => string.IsNullOrEmpty(_mName) ? "External Tool" : _mName;
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _mName = value;
                    return;
                }
                _mName = "External Tool";
            }
        }

        public string Description
        {
            get => string.IsNullOrEmpty(field) ? "\"\"" : field;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    field = "\"\"";
                    return;
                }
                field = value;
            }
        }

        public VisibilityMode VisibilityMode { get; set; }

        public bool Save { get; set; }

        public bool Hidden { get; set; }

        public TransactionMode? TransactionMode { get; set; }

        public RegenerationOption? RegenerationMode { get; set; }

        public JournalingMode? JournalingMode { get; set; }

        public override string ToString()
        {
            return _mName;
        }

        protected string MAssemblyPath;
        protected Guid MClientId;
        private string _mName;
    }
}
