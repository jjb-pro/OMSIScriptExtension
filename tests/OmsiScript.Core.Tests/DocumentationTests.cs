using OMSIScript.Analysis;
using OMSIScript.Documentation;
using OMSIScript.Language;

namespace OMSIScript.Core.Tests;

public sealed class DocumentationTests
{
    public static TheoryData<string> Keys()
    {
        var keys = new TheoryData<string>();
        foreach (var op in OperatorCatalog.FloatOperators.Concat(OperatorCatalog.StringOperators))
            keys.Add(op.DocumentationKey);
        foreach (var keyword in KeywordCatalog.Keywords.Append(KeywordCatalog.Macro).Append(KeywordCatalog.Trigger))
            keys.Add(keyword.DocumentationKey);
        foreach (var name in GameData.Default.SystemVariableList.Names)
            keys.Add("sysvar." + name);
        foreach (var name in KeyboardTriggers.EngineTriggers)
            keys.Add("trigger." + name);
        return keys;
    }

    public static TheoryData<string, string> Variables()
    {
        var data = new TheoryData<string, string>();
        foreach (var kind in new[] { ObjectKind.Vehicle, ObjectKind.SceneryObject, ObjectKind.Human })
        {
            foreach (var name in GameData.Default.For(kind).FloatVariables.Names.Where(n => n != "TrafficPriorityWarningNeeded"))
                data.Add(kind.ToString(), name);
        }

        foreach (var name in GameData.Default.Vehicle.StringVariables.Names)
            data.Add("String", name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Variables))]
    public void PredefinedVariableHasDocumentation(string kind, string name)
    {
        var text = kind == "String"
            ? Docs.Variable(ObjectKind.Vehicle, name, isString: true)
            : Docs.Variable(Enum.Parse<ObjectKind>(kind), name, isString: false);
        Assert.False(string.IsNullOrWhiteSpace(text), name);
    }

    [Theory]
    [InlineData("Wheel_Rotation_2_R")]
    [InlineData("PAX_Exit3_Req")]
    [InlineData("contactshoe_1_volt_rail")]
    [InlineData("articulation_0_alpha")]
    public void IndexedVariablesUsePatterns(string name)
        => Assert.NotNull(Docs.Variable(ObjectKind.Vehicle, name, isString: false));

    [Fact]
    public void IndexedTriggersUsePatterns()
        => Assert.NotNull(Docs.Trigger("railbond_3"));

    [Theory]
    [MemberData(nameof(Keys))]
    public void HasDocumentation(string key) => Assert.False(string.IsNullOrWhiteSpace(Docs.Get(key)), key);

    [Fact]
    public void EverySystemMacroOfTheGameIsCatalogued()
    {
        var names = GameData.Default.Vehicle.Macros.Names.Concat(GameData.Default.SceneryObject.Macros.Names).Distinct();
        Assert.All(names, name =>
        {
            var info = SystemMacroCatalog.Find(name);
            Assert.NotNull(info);
            Assert.False(string.IsNullOrWhiteSpace(Docs.Get(info.DocumentationKey)), name);
        });
    }

    [Fact]
    public void DiagnosticCodesAreUnique()
        => Assert.Equal(DiagnosticCodes.All.Count, DiagnosticCodes.All.Select(d => d.Code).Distinct().Count());

    [Fact]
    public void MessageFormatsAreValid()
        => Assert.All(DiagnosticCodes.All, d => d.Format(["a", "b", "c"]));
}
