using System;

namespace AddInManager.Models
{
    [Flags]
    public enum AddinType
    {
        Invalid = 0,
        Command = 1,
        Application = 2,
        Mixed = 3
    }
}
