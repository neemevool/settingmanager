using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class ProcessAndTableTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetProcesses_ReturnsAliasVariantsAndTableCount()
    {
        var processes = await fixture.Sm.GetProcessesAsync(SqlServerFixture.ClientName);

        var tproc = Assert.Single(processes, p => p.Name == "tproc");
        Assert.Equal("TaskDocument", tproc.ProcessType);
        Assert.Equal("tprocvariant", tproc.Variants);
        Assert.Equal(5, tproc.TableCount);
        var variant = Assert.Single(processes, p => p.Name == "tprocvariant");
        Assert.Equal("tproc", variant.SettingProcedures);
        Assert.Equal(0, variant.TableCount);
    }

    [Fact]
    public async Task GetTables_ClassifiesLayersOverridesAndBackendTable()
    {
        var tables = await fixture.Sm.GetTablesAsync(SqlServerFixture.ClientName, "tproc");

        Assert.Equal(
            [
                ("DocumentItemsListLayout", "base", "tprocDocumentItemsListLayout", 1, "GettprocDocumentItemsListLayoutInfo", true),
                ("DocumentListLayout", "base", "tprocDocumentListLayout", 8, null, false),
                ("DocumentListLayout", "env:wterminal2", "tprocDocumentListLayoutwterminal2", 5, null, false),
                ("DocumentListLayout", "lang:en", "tprocDocumentListLayouten", 3, null, false),
                ("ProcessSettings", "base", "tprocProcessSettings", 1, "GettprocProcessSettingsInfo-", false),
                ("ServerProcessSettings", "base", "ServerProcessSettings", 5, null, false),
            ],
            tables.Select(t => (t.View, t.Layer, t.PhysicalTable, t.RowCount, t.OverrideProcedure, t.OverrideActive)).ToArray());
        Assert.All(tables, t => Assert.Equal("tproc", t.Owner));
    }

    [Fact]
    public async Task GetTables_AliasWithoutOwnTables_ReturnsParentTables()
    {
        var tables = await fixture.Sm.GetTablesAsync(SqlServerFixture.ClientName, "tprocvariant");

        var layout = Assert.Single(tables, t => t.View == "DocumentListLayout" && t.Layer == "base");
        Assert.Equal("tproc", layout.Owner);
        Assert.Equal("tprocDocumentListLayout", layout.PhysicalTable);
        var backend = Assert.Single(tables, t => t.View == "ServerProcessSettings");
        Assert.Equal("tprocvariant", backend.Owner);
        Assert.Equal(1, backend.RowCount);
    }

    [Fact]
    public async Task GetTables_UnknownProcess_ThrowsProcessNotFound()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetTablesAsync(SqlServerFixture.ClientName, "nosuch"));
        Assert.Equal(SmErrorCodes.ProcessNotFound, ex.Code);
    }

    [Fact]
    public async Task GetTables_UnsafeName_ThrowsInvalidName()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetTablesAsync(SqlServerFixture.ClientName, "x;drop"));
        Assert.Equal(SmErrorCodes.InvalidName, ex.Code);
    }
}
