using System;
using StarterNG.Classes;

namespace StarterNG.Views;

public static class CouplingBits
{
    public static readonly string[] BitKeys =
        { "CplMechanical", "CplBrake", "CplControl", "CplHighVoltage",
          "CplGangway", "CplAuxPneumatic", "CplHeating", "CplWorkshop" };
}

public static class CrewChoices
{
    public static readonly (eDriverType Type, string Key)[] All =
    {
        (eDriverType.Headdriver, "DriverHead"), (eDriverType.Reardriver, "DriverRear"),
        (eDriverType.Passenger, "DriverPassenger"), (eDriverType.Nobody, "DriverNobody")
    };

    public static int IndexOf(eDriverType type) => Array.FindIndex(All, choice => choice.Type == type);

    /// <summary>The crew in that place of the list; nobody for no place at all, as a list with nothing selected has.</summary>
    public static eDriverType At(int index) => index >= 0 && index < All.Length ? All[index].Type : eDriverType.Nobody;
}
