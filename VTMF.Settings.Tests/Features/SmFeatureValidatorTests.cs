using VTMF.Settings.Core.Sm.Features;

namespace VTMF.Settings.Tests.Features;

public sealed class SmFeatureValidatorTests
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

    private static SmFeatureRow Row(string value = "v", string name = "n") =>
        new() { Table = "ProcessSettings", Name = name, Value = value };

    private static SmFeatureParam Text(string name, string description = "desc") =>
        new() { Name = name, Description = description, Type = "Text" };

    [Fact]
    public void Find_IsCaseInsensitiveAndDistinct()
    {
        var found = FeaturePlaceholders.Find("{{Name}}.{{name}}");

        Assert.Single(found);
        Assert.Contains("NAME", found);
    }

    [Fact]
    public void Substitute_ReplacesAllOccurrences()
    {
        var result = FeaturePlaceholders.Substitute("{{a}}-{{A}}-{{b}}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a"] = "1" });

        Assert.Equal("1-1-{{b}}", result);
    }

    [Fact]
    public void Validate_UndeclaredPlaceholder_FailsFinishOnly()
    {
        var feature = Feature(rows: [Row("{{name}}")]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: false).Errors);
        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: true).Errors);
        Assert.Contains("name", error);
    }

    [Fact]
    public void Validate_ServerSettingRow_Fails()
    {
        var feature = Feature(rows: [new SmFeatureRow { Table = "t", Name = "n", Value = "v", SettingType = "ServerSetting" }]);

        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: false).Errors);

        Assert.Contains("ServerSetting rows cannot be part of a feature", error);
    }

    [Fact]
    public void Validate_ListWithoutValues_Fails()
    {
        var feature = Feature(parameters: [new SmFeatureParam { Name = "p", Description = "d", Type = "List" }]);

        Assert.NotEmpty(SmFeatureValidator.Validate(feature, finish: false).Errors);
    }

    [Fact]
    public void Validate_InvalidDefaultForType_FailsFinish()
    {
        var number = new SmFeatureParam { Name = "n", Description = "d", Type = "Number", Default = "abc" };
        var boolean = new SmFeatureParam { Name = "b", Description = "d", Type = "Boolean", Default = "yes" };
        var list = new SmFeatureParam
        {
            Name = "l", Description = "d", Type = "List", Default = "Z",
            Values = [new SmFeatureParamValue { Value = "A", Description = "a" }],
        };
        var feature = Feature(rows: [Row("{{n}}{{b}}{{l}}")], parameters: [number, boolean, list]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: false).Errors);
        var errors = SmFeatureValidator.Validate(feature, finish: true).Errors;

        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Validate_ValidDefaults_PassFinish()
    {
        var feature = Feature(
            rows: [Row("{{n}}{{b}}")],
            parameters:
            [
                new SmFeatureParam { Name = "n", Description = "d", Type = "Number", Default = "1.5" },
                new SmFeatureParam { Name = "b", Description = "d", Type = "Boolean", Default = "true" },
            ]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: true).Errors);
    }

    [Fact]
    public void Validate_BuiltInsNeedNoParam()
    {
        var feature = Feature(rows: [Row("{{processname}} {{ClientName}}")]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: true).Errors);
    }

    [Fact]
    public void Validate_UnusedParam_Warns()
    {
        var feature = Feature(rows: [Row()], parameters: [Text("p")]);

        var result = SmFeatureValidator.Validate(feature, finish: true);

        Assert.Empty(result.Errors);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("PARAM_UNUSED", warning.Code);
        Assert.Equal("Warning", warning.Severity);
    }

    [Fact]
    public void Validate_PlaceholderInStepSql_NeedsParam()
    {
        var feature = Feature(steps: [new SmFeatureStep { Text = "t", Sql = "SELECT '{{x}}'" }]);

        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: true).Errors);
        Assert.Contains("x", error);

        var declared = Feature(steps: [new SmFeatureStep { Text = "t", Sql = "SELECT '{{x}}'" }], parameters: [Text("x")]);
        Assert.Empty(SmFeatureValidator.Validate(declared, finish: true).Errors);
    }

    [Fact]
    public void Validate_BasicSaveRules()
    {
        var feature = new SmFeature
        {
            Name = "",
            Rows =
            [
                new SmFeatureRow { Table = "", Name = "", Value = "v" },
                new SmFeatureRow { Table = "a{{x}}", Name = "n", Value = "v" },
                new SmFeatureRow { Table = "t", Name = "n", Value = "v", Layer = "bogus" },
                new SmFeatureRow { Table = "t", Name = "n", Value = "v", Layer = "env:{{x}}" },
                new SmFeatureRow { Table = "t", Name = "n", Value = null },
                new SmFeatureRow { Table = "t", Name = "n", Op = "remove" },
                new SmFeatureRow { Table = "t", Name = "n", Value = "v", Op = "zap" },
                new SmFeatureRow { Table = "t", Name = "n", Value = "v", SettingType = "Other" },
            ],
            Params =
            [
                Text("1bad"), Text("dup"), Text("DUP"), Text("ProcessName"),
                new SmFeatureParam { Name = "t", Description = "d", Type = "Weird" },
            ],
        };

        var errors = SmFeatureValidator.Validate(feature, finish: false).Errors;

        // name, row1 table, row1 name, row2 table, row3 layer, row5 value, row7 op, row8 type,
        // param 1bad, duplicate, builtin, type
        Assert.Equal(12, errors.Count);
    }

    [Fact]
    public void Validate_FinishNeedsDescriptionAndContent()
    {
        var feature = new SmFeature { Name = "f" };

        var errors = SmFeatureValidator.Validate(feature, finish: true).Errors;

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Validate_FinishNeedsParamAndListValueDescriptions()
    {
        var feature = Feature(
            rows: [Row("{{p}}")],
            parameters:
            [
                new SmFeatureParam
                {
                    Name = "p", Description = "", Type = "List",
                    Values = [new SmFeatureParamValue { Value = "A", Description = "" }],
                },
            ]);

        Assert.Equal(2, SmFeatureValidator.Validate(feature, finish: true).Errors.Count);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("1,000")]
    public void Validate_NumberDefaultWithSeparator_FailsFinish(string def)
    {
        var feature = Feature(rows: [Row("{{n}}")], parameters: [new SmFeatureParam { Name = "n", Description = "d", Type = "Number", Default = def }]);

        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: true).Errors);

        Assert.Contains("default", error);
    }

    [Fact]
    public void Validate_NumberDefaultWithWhitespace_PassesFinish()
    {
        var feature = Feature(rows: [Row("{{n}}")], parameters: [new SmFeatureParam { Name = "n", Description = "d", Type = "Number", Default = " 2.5 " }]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: true).Errors);
    }

    private static SmFeatureRow Group(string name, string value, string? after, string op = "set") =>
        new() { Table = "DocumentListLayout", Name = name, Value = value, SettingType = "SettingGroup", After = after, Op = op };

    private static SmFeatureParam Position(string name = "pos") =>
        new() { Name = name, Description = "After which column should the barcode column be shown? (or first)", Type = "Position" };

    [Fact]
    public void Save_PositionParam_UsedOutsideGroupAfter_IsError()
    {
        var feature = Feature(rows: [Row("{{pos}}")], parameters: [Position()]);

        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: false).Errors);

        Assert.Contains("pos", error);
    }

    [Fact]
    public void Save_PositionParam_InSettingAfterOrPartOfGroupAfter_IsError()
    {
        var feature = Feature(
            rows:
            [
                new SmFeatureRow { Table = "DocumentListLayout", Name = "Grid.X", Value = "1", After = "{{pos}}" },
                Group("Column", "A", "Column={{pos}}"),
            ],
            parameters: [Position()]);

        var errors = SmFeatureValidator.Validate(feature, finish: false).Errors;

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Contains("pos", e));
    }

    [Fact]
    public void Save_PositionParam_WithDefaultOrValues_IsError()
    {
        var feature = Feature(
            rows: [Group("Column", "BARCODE", "{{pos}}"), Group("Column", "QTY", "{{pos2}}")],
            parameters:
            [
                new SmFeatureParam { Name = "pos", Description = "q", Type = "Position", Default = "first" },
                new SmFeatureParam
                {
                    Name = "pos2", Description = "q", Type = "Position",
                    Values = [new SmFeatureParamValue { Value = "DOCNR", Description = "d" }],
                },
            ]);

        var errors = SmFeatureValidator.Validate(feature, finish: false).Errors;

        Assert.Contains(errors, e => e.Contains("'pos'", StringComparison.Ordinal) && e.Contains("default", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'pos2'", StringComparison.Ordinal) && e.Contains("values", StringComparison.Ordinal));
    }

    [Fact]
    public void Finish_GroupSetRowWithoutAfter_IsError()
    {
        var feature = Feature(rows: [Group("Column", "BARCODE", null)]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: false).Errors);
        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: true).Errors);
        Assert.Contains("position", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Row 1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Finish_GroupSetRowWithLiteralAnchor_IsError()
    {
        var feature = Feature(rows: [Group("Column", "BARCODE", "Column=DOCNR")]);

        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: true).Errors);

        Assert.Contains("position", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Finish_GroupSetRowWithPositionParam_IsValid()
    {
        var feature = Feature(rows: [Group("Column", "BARCODE", " {{pos}} ")], parameters: [Position()]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: true).Errors);
    }

    [Fact]
    public void Finish_GroupSetRowWithTextParamAsAfter_IsError()
    {
        var feature = Feature(rows: [Group("Column", "BARCODE", "{{pos}}")], parameters: [Text("pos")]);

        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: true).Errors);

        Assert.Contains("position", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Finish_GroupChainedToSameFeatureGroup_IsValid()
    {
        var feature = Feature(
            rows:
            [
                Group("Column", "{{name}}", "{{pos}}"),
                Group("Column", "{{name}}2", "Column={{name}}"),
                Group("Detail.{{name}}", "x", "{{pos}}"),
                Group("Detail.{{name}}Extra", "x", "Detail.{{name}}"),
            ],
            parameters: [Position(), Text("name")]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: true).Errors);
    }

    [Fact]
    public void Finish_GroupChainedToRemovedOrSelf_IsError()
    {
        var feature = Feature(
            rows:
            [
                Group("Column", "OLD", null, op: "remove"),
                Group("Column", "A", "Column=OLD"),
                Group("Column", "B", "Column=B"),
            ]);

        var errors = SmFeatureValidator.Validate(feature, finish: true).Errors;

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("Row 2", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Row 3", StringComparison.Ordinal));
    }

    // The plan places rows in feature row order, so a chained anchor must name a group row that comes earlier.
    [Fact]
    public void Finish_GroupChainedToLaterRow_IsError()
    {
        var feature = Feature(
            rows:
            [
                Group("Column", "B", "Column=A"),
                Group("Column", "A", "{{pos}}"),
            ],
            parameters: [Position()]);

        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: true).Errors);

        Assert.Contains("Row 1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Finish_GroupChainCycle_IsError()
    {
        var feature = Feature(
            rows:
            [
                Group("Column", "A", "Column=B"),
                Group("Column", "B", "Column=A"),
            ]);

        var error = Assert.Single(SmFeatureValidator.Validate(feature, finish: true).Errors);

        Assert.Contains("Row 1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Finish_GroupRemoveRow_NeedsNoPosition()
    {
        var feature = Feature(rows: [Group("Column", "BARCODE", null, op: "remove")]);

        Assert.Empty(SmFeatureValidator.Validate(feature, finish: true).Errors);
    }

    [Fact]
    public void PositionErrors_ReportsOnlyThePositionRule()
    {
        var feature = new SmFeature { Name = "f", Rows = [Group("Column", "BARCODE", null)] };

        var error = Assert.Single(SmFeatureValidator.PositionErrors(feature));

        Assert.Contains("Row 1", error, StringComparison.Ordinal);
    }
}
