
using Autodesk.Revit.Attributes;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using System;

namespace AddInManager
{
    [Transaction(TransactionMode.Manual)]
    public class CAddInManagerFaceless : IExternalCommand
    {
        public Result Execute(ExternalCommandData revit, ref string message, ElementSet elements)
        {
            return AIM.Instance.ExecuteCommand(revit, ref message, elements, true);
        }
    }
}
