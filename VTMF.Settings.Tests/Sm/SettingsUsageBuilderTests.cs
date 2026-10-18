using VTMF.Settings.Core.Sm.Usage;

namespace VTMF.Settings.Tests.Sm;

public sealed class SettingsUsageBuilderTests
{
    private const string GetDocumentsFortprocBody = """
        CREATE PROCEDURE dbo.GetDocumentsFortproc @workercode nvarchar(250)
        AS
        BEGIN
            declare @psv dbo.processSettings
            insert into @psv select * from dbo.GetProcessSettingValues('tproc', @workercode)
            declare @new nvarchar(30) = dbo.GetPSV(@psv, 'Document.State.Terminal.New', 'UUS')
            declare @join nvarchar(10) = dbo.GetPSVB(@psv,'Document.Query.Join','False')
            declare @dyn nvarchar(30) = dbo.GetPSV(@psv, @new, 'x')
            select @new, @join, @dyn
        END
        """;

    private const string RegDocStateFortprocFinishedBody = """
        CREATE PROCEDURE dbo.RegDocStateFortprocFinished @workercode nvarchar(250)
        AS
        BEGIN
            declare @remove nvarchar(10) = dbo.GetProcessSettingValue('tproc','Document.RegDocState.RemoveNoQty',@workercode,'False')
            declare @key nvarchar(400) = (select top 1 VALUE from dbo.ServerProcessSettings where NAME = 'tproc.Directo.ApiKey')
            select @remove, @key
        END
        """;

    private const string GetDocumentsFortotherBody = """
        CREATE PROCEDURE dbo.GetDocumentsFortother @workercode nvarchar(250)
        AS
        BEGIN
            declare @psv dbo.processSettings
            insert into @psv select * from dbo.GetProcessSettingValues('tother', @workercode)
            select dbo.GetPSV(@psv, 'Other.Setting', 'o')
        END
        """;

    private const string GetVariantExtrasBody = """
        CREATE PROCEDURE dbo.GetVariantExtras @workercode nvarchar(250)
        AS
        BEGIN
            declare @psv dbo.processSettings
            insert into @psv select * from dbo.GetProcessSettingValues('tprocvariant', @workercode)
            declare @unused nvarchar(30) = dbo.GetPSV(@psv, 'Unused.Setting', 'dflt')
            declare @legacy nvarchar(30) = dbo.GetProcessSettingValue('tprocvariant','Unused.Setting',@workercode,'legacydflt')
            select @unused, @legacy
        END
        """;

    private static SmUsageModule Module(string name, string body) => new() { ProcName = name, Definition = body };

    private static SmUsageProcess Process(string name, string? settingProcedures, bool inScope) =>
        new() { Name = name, SettingProcedures = settingProcedures, InScope = inScope };

    private static SmUsageSetting Setting(string process, string name, string workerCode, string? value, bool isEmpty = false) =>
        new() { Process = process, SettingName = name, WorkerCode = workerCode, Value = value, IsEmpty = isEmpty };

    private static SmUsageSources SeedTprocSources() => new(
        [Process("tproc", null, true)],
        [
            Module("GetDocumentsFortproc", GetDocumentsFortprocBody),
            Module("RegDocStateFortprocFinished", RegDocStateFortprocFinishedBody),
            Module("GetDocumentsFortother", GetDocumentsFortotherBody),
        ],
        [
            Setting("tproc", "Document.State.Terminal.New", "ALL", "NEW"),
            Setting("tproc", "Document.Query.Join", "ALL", "True"),
            Setting("tproc", "Document.Query.Join", "worker1", "False"),
            Setting("tproc", "Unused.Setting", "ALL", "1"),
            Setting("tproc", "Directo.ApiKey", "ALL", "********"),
        ]);

    [Fact]
    public void Build_SeedTproc_MatchesIntegrationContract()
    {
        var usage = SettingsUsageBuilder.Build("tproc", null, SeedTprocSources());

        Assert.Equal(
            [
                ("Directo.ApiKey", null, "none", null, "********", true),
                ("Document.Query.Join", "GetDocumentsFortproc", "psv", "False", "True", false),
                ("Document.RegDocState.RemoveNoQty", "RegDocStateFortprocFinished", "legacy", "False", null, false),
                ("Document.State.Terminal.New", "GetDocumentsFortproc", "psv", "UUS", "NEW", false),
                ("Unused.Setting", null, "none", null, "1", true),
                (null, "GetDocumentsFortproc", "unparsed", null, null, false),
                (null, "RegDocStateFortprocFinished", "direct", null, null, false),
            ],
            usage.Select(u => (u.SettingName, u.ProcedureName, u.CallStyle, u.DefaultInCode, u.ConfiguredValue, u.Unused)).ToArray());
        Assert.Equal(1, usage.Single(u => u.SettingName == "Document.Query.Join").WorkerOverrides);
        Assert.Equal("1", usage.Single(u => u.CallStyle == "unparsed").Detail);
        Assert.Equal("ServerProcessSettings", usage.Single(u => u.CallStyle == "direct").Detail);
        Assert.DoesNotContain(usage, u => u.ProcedureName == "GetDocumentsFortother");
        Assert.Equal("literal", usage.Single(u => u.SettingName == "Document.Query.Join").Attribution);
        Assert.Equal("process", usage.Single(u => u.SettingName == "Unused.Setting").ConfiguredIn);
    }

    [Fact]
    public void Build_EmptyValueFromSql_HidesParentForPsv()
    {
        var sources = new SmUsageSources(
            [Process("tprocvariant", "tproc", true), Process("tproc", null, true)],
            [Module("GetVariantExtras", GetVariantExtrasBody)],
            [
                Setting("tprocvariant", "Unused.Setting", "ALL", "", isEmpty: true),
                Setting("tproc", "Unused.Setting", "ALL", "1"),
            ]);

        var usage = SettingsUsageBuilder.Build("tprocvariant", null, sources);

        Assert.Equal(
            [
                ("Unused.Setting", "GetVariantExtras", "legacy", "legacydflt", "1", "parent", 0),
                ("Unused.Setting", "GetVariantExtras", "psv", "dflt", null, null, 0),
            ],
            usage.Select(u => (u.SettingName, u.ProcedureName, u.CallStyle, u.DefaultInCode, u.ConfiguredValue, u.ConfiguredIn, u.WorkerOverrides)).ToArray());
    }

    [Fact]
    public void Build_LegacyCall_ReadsMappedProcessRows()
    {
        var sources = new SmUsageSources(
            [Process("c", "b", true), Process("b", "a", true), Process("a", null, false)],
            [Module("LegacyReader", "select dbo.GetProcessSettingValue('b', 'S', @w, 'd')")],
            [Setting("a", "S", "ALL", "va")]);

        var usage = SettingsUsageBuilder.Build("c", null, sources);

        var row = Assert.Single(usage, u => u.CallStyle == "legacy");
        Assert.Equal(("S", "literal", "d", "va", "parent"), (row.SettingName, row.Attribution, row.DefaultInCode, row.ConfiguredValue, row.ConfiguredIn));
    }

    [Fact]
    public void Build_MatchesNamesCaseInsensitively()
    {
        var sources = new SmUsageSources(
            [Process("tproc", null, true)],
            [Module("GetDocumentsFortproc", "declare @j nvarchar(10) = dbo.getpsv(@psv, 'document.query.join', 'False')")],
            [Setting("tproc", "Document.Query.Join", "ALL", "True")]);

        var usage = SettingsUsageBuilder.Build("TPROC", null, sources);

        var row = Assert.Single(usage, u => u.CallStyle == "psv");
        Assert.Equal(("procname", "True", "process"), (row.Attribution, row.ConfiguredValue, row.ConfiguredIn));
        Assert.DoesNotContain(usage, u => u.CallStyle == "none");
    }

    [Fact]
    public void Build_NamePrefix_ExcludesRowsWithoutSettingName()
    {
        var usage = SettingsUsageBuilder.Build("tproc", "Document.", SeedTprocSources());

        Assert.NotEmpty(usage);
        Assert.DoesNotContain(usage, u => u.CallStyle is "unparsed" or "direct");
        Assert.All(usage, u => Assert.StartsWith("Document.", u.SettingName, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WorkerOverridesCountOnlyRequestedProcessForPsv()
    {
        var sources = new SmUsageSources(
            [Process("tprocvariant", "tproc", true), Process("tproc", null, true)],
            [Module("GetDocumentsFortproc", GetDocumentsFortprocBody)],
            [
                Setting("tproc", "Document.Query.Join", "ALL", "True"),
                Setting("tproc", "Document.Query.Join", "worker1", "False"),
                Setting("tprocvariant", "Document.State.Terminal.New", "ALL", "VNEW"),
            ]);

        var usage = SettingsUsageBuilder.Build("tprocvariant", null, sources);

        var join = usage.Single(u => u.SettingName == "Document.Query.Join");
        Assert.Equal(("psv", "True", "parent", 0), (join.CallStyle, join.ConfiguredValue, join.ConfiguredIn, join.WorkerOverrides));
        var newState = usage.Single(u => u.SettingName == "Document.State.Terminal.New");
        Assert.Equal(("VNEW", "process"), (newState.ConfiguredValue, newState.ConfiguredIn));
    }
}
