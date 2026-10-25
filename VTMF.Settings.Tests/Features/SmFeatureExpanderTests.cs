using VTMF.Settings.Core.Sm;
using VTMF.Settings.Core.Sm.Features;

namespace VTMF.Settings.Tests.Features;

public sealed class SmFeatureExpanderTests
{
    private static SmFeature Feature(
        IReadOnlyList<SmFeatureRow>? rows = null,
        IReadOnlyList<SmFeatureParam>? parameters = null,
        IReadOnlyList<SmFeatureStep>? steps = null) => new()
    {
        Name = "f",
        Description = "d",
        Rows = rows ?? [],
        Params = parameters ?? [],
        Steps = steps ?? [],
    };

    private static SmFeatureRow Row(string name, string? value = "v", string layer = "base", string op = "set") =>
        new() { Table = "ProcessSettings", Layer = layer, Name = name, Value = value, Op = op };

    private static SmFeatureParam Param(string name, string type = "Text", string? def = null, params string[] values) =>
        new()
        {
            Name = name,
            Description = "desc",
            Type = type,
            Default = def,
            Values = [.. values.Select(v => new SmFeatureParamValue { Value = v, Description = "d" })],
        };

    private static Dictionary<string, string> Answers(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Expand_BarcodeExample_DependentNames()
    {
        var feature = Feature(
            rows:
            [
                Row("Function.Barcode.Notfound.Action", "{{notfoundAction}}"),
                Row("Function.{{notfoundAction}}.Notfound.ItemType", "Option"),
                Row("Function.Barcode.Notfound.{{notfoundAction}}.Procedure", "RowSelectionOptionsQuestion"),
            ],
            parameters: [Param("notfoundAction", "List", null, "OptionsQuestion", "Ignore")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("notfoundAction", "optionsquestion")));

        Assert.Equal("Function.Barcode.Notfound.Action", result.Desired.Rows[0].Name);
        Assert.Equal("OptionsQuestion", result.Desired.Rows[0].Value);
        Assert.Equal("Function.OptionsQuestion.Notfound.ItemType", result.Desired.Rows[1].Name);
        Assert.Equal("Function.Barcode.Notfound.OptionsQuestion.Procedure", result.Desired.Rows[2].Name);
    }

    [Fact]
    public void Expand_PlaceholderCaseInsensitive_FillsAll()
    {
        var feature = Feature(
            rows: [Row("Column", "{{Name}}"), Row("Column.{{name}}.Caption", "{{NAME}}")],
            parameters: [Param("name")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("NAME", "Foo")));

        Assert.Equal("Foo", result.Desired.Rows[0].Value);
        Assert.Equal("Column.Foo.Caption", result.Desired.Rows[1].Name);
        Assert.Equal("Foo", result.Desired.Rows[1].Value);
    }

    [Fact]
    public void Expand_UsesDefaults()
    {
        var feature = Feature(rows: [Row("n", "{{x}}")], parameters: [Param("x", "Text", "dflt")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", null);

        Assert.Equal("dflt", result.Desired.Rows[0].Value);
        Assert.Equal("dflt", result.Values["x"]);
    }

    [Fact]
    public void Expand_Missing_Throws()
    {
        var feature = Feature(parameters: [Param("a"), Param("b"), Param("c", "Text", "ok")]);

        var ex = Assert.Throws<SmException>(() => SmFeatureExpander.Expand(feature, "p", "c", null));

        Assert.Equal(SmErrorCodes.FeatureParamMissing, ex.Code);
        Assert.Contains("a, b", ex.Message);
    }

    [Theory]
    [InlineData("Number", "x")]
    [InlineData("Boolean", "yes")]
    [InlineData("List", "Other")]
    public void Expand_InvalidTypes_Throw(string type, string answer)
    {
        var feature = Feature(parameters: [Param("p1", type, null, "One", "Two")]);

        var ex = Assert.Throws<SmException>(() =>
            SmFeatureExpander.Expand(feature, "p", "c", Answers(("p1", answer))));

        Assert.Equal(SmErrorCodes.FeatureParamInvalid, ex.Code);
        Assert.Contains("p1", ex.Message);
        if (type == "List")
        {
            Assert.Contains("One", ex.Message);
        }
    }

    [Fact]
    public void Expand_NumberKeepsGivenText()
    {
        var feature = Feature(parameters: [Param("n", "Number")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("n", "1.50")));

        Assert.Equal("1.50", result.Values["n"]);
    }

    [Fact]
    public void Expand_BooleanNormalized()
    {
        var feature = Feature(rows: [Row("n", "{{b}}")], parameters: [Param("b", "Boolean")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("b", "TRUE")));

        Assert.Equal("True", result.Desired.Rows[0].Value);
    }

    [Fact]
    public void Expand_RemoveRow_MapsToRemove()
    {
        var feature = Feature(rows: [Row("a", op: "remove"), Row("b")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", null);

        Assert.Equal("remove", result.Desired.Rows[0].Op);
        Assert.Equal("upsert", result.Desired.Rows[1].Op);
    }

    [Fact]
    public void Expand_LayerPlaceholder()
    {
        var feature = Feature(rows: [Row("n", layer: "env:{{env}}")], parameters: [Param("env")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("env", "PROD")));

        Assert.Equal("env:PROD", result.Desired.Rows[0].Layer);
    }

    [Fact]
    public void Expand_BuiltIns()
    {
        var feature = Feature(steps: [new SmFeatureStep { Text = "run for {{clientname}}", Sql = "SELECT '{{processname}}'" }]);

        var result = SmFeatureExpander.Expand(feature, "proc", "cli", null);

        var step = Assert.Single(result.Steps);
        Assert.Equal("SELECT 'proc'", step.Sql);
        Assert.Equal("run for cli", step.Text);
    }

    [Fact]
    public void Expand_IsMergeWithoutOrderNr()
    {
        var feature = Feature(rows: [Row("a")]);

        var result = SmFeatureExpander.Expand(feature, "proc", "c", null);

        Assert.Equal("proc", result.Desired.Process);
        Assert.Equal("merge", result.Desired.Mode);
        Assert.Empty(result.Desired.Scope);
        Assert.Null(result.Desired.Rows[0].OrderNr);
    }

    [Fact]
    public void Expand_UnknownAnswerKey_Throws()
    {
        var feature = Feature(parameters: [Param("alpha", "Text", "d"), Param("beta", "Text", "d")]);

        var ex = Assert.Throws<SmException>(() =>
            SmFeatureExpander.Expand(feature, "p", "c", Answers(("alpah", "x"))));

        Assert.Equal(SmErrorCodes.FeatureParamInvalid, ex.Code);
        Assert.Contains("Unknown parameter 'alpah'; expected: alpha, beta", ex.Message);
    }

    [Fact]
    public void Expand_BuiltInAnswerKey_IsNotUnknown()
    {
        var result = SmFeatureExpander.Expand(Feature(), "p", "c", Answers(("ProcessName", "x")));

        Assert.Equal("p", result.Values["processname"]);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("1,000")]
    [InlineData("1 000")]
    public void Expand_NumberWithSeparators_Throws(string answer)
    {
        var feature = Feature(parameters: [Param("n", "Number")]);

        var ex = Assert.Throws<SmException>(() => SmFeatureExpander.Expand(feature, "p", "c", Answers(("n", answer))));

        Assert.Equal(SmErrorCodes.FeatureParamInvalid, ex.Code);
    }

    [Fact]
    public void Expand_NumberWithWhitespace_IsTrimmed()
    {
        var feature = Feature(parameters: [Param("n", "Number")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("n", " 2.5 ")));

        Assert.Equal("2.5", result.Values["n"]);
    }

    [Fact]
    public void Expand_NullAnswer_FallsBackToDefault()
    {
        var feature = Feature(parameters: [Param("b", "Text", "dflt")]);

        var result = SmFeatureExpander.Expand(feature, "p", "c", new Dictionary<string, string> { ["b"] = null! });

        Assert.Equal("dflt", result.Values["b"]);
    }

    [Fact]
    public void Expand_NullAnswerWithoutDefault_IsMissing()
    {
        var feature = Feature(parameters: [Param("b", "Number")]);

        var ex = Assert.Throws<SmException>(() =>
            SmFeatureExpander.Expand(feature, "p", "c", new Dictionary<string, string> { ["b"] = null! }));

        Assert.Equal(SmErrorCodes.FeatureParamMissing, ex.Code);
    }

    private static SmFeatureRow Group(string name, string value, string? after) =>
        new() { Table = "DocumentListLayout", Name = name, Value = value, SettingType = "SettingGroup", After = after };

    private static SmFeature PositionFeature(SmFeatureRow group) => Feature(
        rows: [group, Group("Column", "{{name}}2", "Column={{name}}")],
        parameters: [Param("pos", "Position"), Param("name")]);

    [Fact]
    public void Expand_PositionFirst_IsCaret()
    {
        var feature = PositionFeature(Group("Column", "{{name}}", "{{pos}}"));

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("pos", "First"), ("name", "BARCODE")));

        Assert.Equal("^", result.Desired.Rows[0].After);
        Assert.Equal("Column=BARCODE", result.Desired.Rows[1].After);
    }

    [Fact]
    public void Expand_PositionValueKeyed()
    {
        var feature = PositionFeature(Group("Column", "{{name}}", "{{pos}}"));

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("pos", "DOCNR"), ("name", "BARCODE")));

        Assert.Equal("Column=DOCNR", result.Desired.Rows[0].After);
        Assert.Equal("DOCNR", result.Values["pos"]);
    }

    [Fact]
    public void Expand_PositionNameKeyed()
    {
        var feature = PositionFeature(Group("Detail.{{name}}", "x", "{{pos}}"));

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("pos", "Lines"), ("name", "Barcode")));

        Assert.Equal("Detail.Lines", result.Desired.Rows[0].After);
    }

    [Fact]
    public void Expand_PositionTrimmed()
    {
        var feature = PositionFeature(Group("Column", "{{name}}", " {{pos}} "));

        var result = SmFeatureExpander.Expand(feature, "p", "c", Answers(("pos", " DOCNR "), ("name", "BARCODE")));

        Assert.Equal("Column=DOCNR", result.Desired.Rows[0].After);
    }

    [Fact]
    public void Expand_PositionEmpty_IsMissing()
    {
        var feature = PositionFeature(Group("Column", "{{name}}", "{{pos}}"));

        foreach (var answer in new[] { "", "  " })
        {
            var ex = Assert.Throws<SmException>(() =>
                SmFeatureExpander.Expand(feature, "p", "c", Answers(("pos", answer), ("name", "BARCODE"))));

            Assert.Equal(SmErrorCodes.FeatureParamMissing, ex.Code);
            Assert.Contains("pos", ex.Message, StringComparison.Ordinal);
        }
    }
}
