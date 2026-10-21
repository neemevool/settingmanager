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
}
