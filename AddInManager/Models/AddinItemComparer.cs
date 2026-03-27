using System;
using System.Collections.Generic;

namespace AddInManager.Models
{
    public class AddinItemComparer : IComparer<AddinItem>
    {
        public int Compare(AddinItem x, AddinItem y) => string.Compare(x?.Name, y?.Name, StringComparison.Ordinal);
    }
}
