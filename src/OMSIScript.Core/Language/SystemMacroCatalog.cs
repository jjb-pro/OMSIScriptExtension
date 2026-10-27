using System.Collections.Frozen;

namespace OMSIScript.Language;

public enum MacroGroup
{
    Vehicle,
    ScriptTexture,
    SceneryObject,
}

public sealed record SystemMacroInfo(string Name, MacroGroup Group, StackEffect Effect)
{
    public string DocumentationKey => "macro." + Name;

    public bool IsAvailableFor(ObjectKind kind) => kind switch
    {
        ObjectKind.Vehicle => Group is MacroGroup.Vehicle or MacroGroup.ScriptTexture,
        ObjectKind.SceneryObject => Group is MacroGroup.SceneryObject,
        ObjectKind.Human => false,
        _ => true,
    };
}

public static class SystemMacroCatalog
{
    public static IReadOnlyList<SystemMacroInfo> All { get; } =
    [
        new("GetRouteIndex", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GetBusstopString", MacroGroup.Vehicle, new(2, 0, 0, 1)),
        new("GetTerminusString", MacroGroup.Vehicle, new(2, 0, 0, 1)),
        new("GetRouteTerminusIndex", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GetTerminusIndex", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GetTerminusCode", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GetBusstopCount", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GiveChangeCoin", MacroGroup.Vehicle, new(1, 0, 0, 0)),
        new("GetRouteBusstopIdent", MacroGroup.Vehicle, new(2, 0, 0, 1)),
        new("GetBusstopIndex", MacroGroup.Vehicle, new(0, 1, 1, 0)),
        new("GetTTBusstopCount", MacroGroup.Vehicle, new(0, 1, 0, 0)),
        new("GetTTBusstopIndex", MacroGroup.Vehicle, new(0, 1, 0, 0)),
        new("GetTTTerminusIndex", MacroGroup.Vehicle, new(0, 1, 0, 0)),
        new("GetTTLineString", MacroGroup.Vehicle, new(0, 0, 0, 1)),
        new("GetTTDelay", MacroGroup.Vehicle, new(0, 1, 0, 0)),
        new("GetTTBusstopName", MacroGroup.Vehicle, new(1, 0, 0, 1)),
        new("GetTTBusstopDep", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GetTTBusstopArr", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GetTicketName", MacroGroup.Vehicle, new(1, 0, 0, 1)),
        new("GetTicketValue", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GetHumanCountOnPathLink", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("NrSpecRandom", MacroGroup.Vehicle, new(1, 1, 0, 0)),
        new("GetHeightAbovePoint", MacroGroup.Vehicle, new(3, 1, 0, 0)),
        new("GetDepotStringGlobal", MacroGroup.Vehicle, new(1, 0, 0, 1)),
        new("GetHumanCountOnSeat", MacroGroup.Vehicle, new(1, 1, 0, 0)),

        new("STNewTex", MacroGroup.ScriptTexture, new(1, 0, 0, 0)),
        new("STLock", MacroGroup.ScriptTexture, new(1, 0, 0, 0)),
        new("STUnlock", MacroGroup.ScriptTexture, new(1, 0, 0, 0)),
        new("STFilter", MacroGroup.ScriptTexture, new(1, 0, 0, 0)),
        new("STSetColor", MacroGroup.ScriptTexture, new(5, 0, 0, 0)),
        new("STDrawPixel", MacroGroup.ScriptTexture, new(3, 0, 0, 0)),
        new("STDrawRect", MacroGroup.ScriptTexture, new(5, 2, 0, 0)),
        new("STTextOut", MacroGroup.ScriptTexture, new(6, 1, 1, 0)),
        new("GetFontIndex", MacroGroup.ScriptTexture, new(0, 1, 1, 0)),
        new("TextLength", MacroGroup.ScriptTexture, new(1, 1, 1, 0)),
        new("STReadPixel", MacroGroup.ScriptTexture, new(3, 0, 0, 0)),
        new("STCopyColor", MacroGroup.ScriptTexture, new(2, 0, 0, 0)),
        new("STLoadTex", MacroGroup.ScriptTexture, new(1, 0, 1, 0)),
        new("STGetR", MacroGroup.ScriptTexture, new(1, 1, 0, 0)),
        new("STGetG", MacroGroup.ScriptTexture, new(1, 1, 0, 0)),
        new("STGetB", MacroGroup.ScriptTexture, new(1, 1, 0, 0)),
        new("STGetA", MacroGroup.ScriptTexture, new(1, 1, 0, 0)),

        new("GetArrBusLine", MacroGroup.SceneryObject, new(1, 0, 0, 1)),
        new("GetArrBusTerminus", MacroGroup.SceneryObject, new(1, 0, 0, 1)),
        new("GetArrBusTimeDiff", MacroGroup.SceneryObject, new(1, 1, 0, 0)),
    ];

    private static readonly FrozenDictionary<string, SystemMacroInfo> ByName = All.ToFrozenDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

    public static SystemMacroInfo? Find(string name) => ByName.GetValueOrDefault(name);
}
