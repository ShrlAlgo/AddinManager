using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Autodesk.Revit.Attributes;

namespace AddInManager
{
    [DataContract]
    public class PersistentAddinStore
    {
        [DataMember]
        public int FormatVersion { get; set; } = 1;

        [DataMember]
        public string RevitVersion { get; set; }

        [DataMember]
        public DateTime LastSaved { get; set; }

        [DataMember]
        public List<PersistentAddin> Commands { get; set; } = new List<PersistentAddin>();

        [DataMember]
        public List<PersistentAddin> Applications { get; set; } = new List<PersistentAddin>();
    }

    [DataContract]
    public class PersistentAddin
    {
        [DataMember]
        public string FilePath { get; set; }

        [DataMember]
        public bool Save { get; set; }

        [DataMember]
        public bool Hidden { get; set; }

        [DataMember]
        public List<PersistentAddinItem> Items { get; set; } = new List<PersistentAddinItem>();
    }

    [DataContract]
    public class PersistentAddinItem
    {
        [DataMember]
        public AddinType AddinType { get; set; }

        [DataMember]
        public string AssemblyPath { get; set; }

        [DataMember]
        public string AssemblyName { get; set; }

        [DataMember]
        public Guid ClientId { get; set; }

        [DataMember]
        public string FullClassName { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }

        [DataMember]
        public VisibilityMode VisibilityMode { get; set; }

        [DataMember]
        public bool Save { get; set; }

        [DataMember]
        public bool Hidden { get; set; }

        [DataMember]
        public TransactionMode? TransactionMode { get; set; }

        [DataMember]
        public RegenerationOption? RegenerationMode { get; set; }

        [DataMember]
        public JournalingMode? JournalingMode { get; set; }
    }
}
