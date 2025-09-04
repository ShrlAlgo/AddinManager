using System;

namespace AddInManager
{
    public interface IAddinNode
    {
        bool Save { get; set; }

        bool Hidden { get; set; }
    }
}
