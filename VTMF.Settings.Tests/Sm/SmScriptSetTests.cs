using VTMF.Settings.Core.Sm;

namespace VTMF.Settings.Tests.Sm;

public sealed class SmScriptSetTests
{
    [Fact]
    public void LoadDefault_ScriptsAreInOutputFolder()
    {
        var set = SmScriptSet.LoadDefault();

        Assert.Equal(40, set.Scripts.Count);
        Assert.Equal("001-script-schema.txt", set.Scripts[0].Name);
        Assert.DoesNotContain(set.Scripts, s => s.Name.StartsWith("999", StringComparison.Ordinal));
        Assert.True(set.Version >= 2);
    }

    [Fact]
    public void LoadMetabase_ScriptsAreInOutputFolder()
    {
        var set = SmScriptSet.LoadMetabase();

        Assert.Equal(7, set.Scripts.Count);
        Assert.Equal("001-script-schema.txt", set.Scripts[0].Name);
        Assert.Equal(SmScriptSet.MetabaseModule, set.ModuleName);
        Assert.Equal(2, set.Version);
    }

    [Fact]
    public void RecordedHash_MatchesScripts_Metabase()
    {
        var set = SmScriptSet.LoadMetabase();

        Assert.Equal(Chunks(set.ComputeSha256()), Chunks(set.RecordedSha256));
    }

    [Fact]
    public void Load_RecordsModuleName()
    {
        Assert.Equal("settingsmanagement", SmScriptSet.LoadDefault().ModuleName);
    }

    [Fact]
    public void RecordedHash_MatchesScripts()
    {
        var set = SmScriptSet.LoadDefault();

        // xunit truncates long strings in failure output; 32-char chunks keep the full new hash visible on the Expected line.
        Assert.Equal(Chunks(set.ComputeSha256()), Chunks(set.RecordedSha256));
    }

    [Fact]
    public void ComputeSha256_IgnoresLineEndings()
    {
        using var crlf = new TempDir();
        using var lf = new TempDir();
        crlf.Write("001-a.txt", "A\r\nB\r\n");
        lf.Write("001-a.txt", "A\nB\n");

        Assert.Equal(SmScriptSet.Load(crlf.Path, SmScriptSet.ClientModule).ComputeSha256(), SmScriptSet.Load(lf.Path, SmScriptSet.ClientModule).ComputeSha256());
    }

    [Fact]
    public void Load_MissingDirectory_ThrowsWithPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "sm-missing-" + Guid.NewGuid().ToString("N"));

        var ex = Assert.Throws<InvalidOperationException>(() => SmScriptSet.Load(path, SmScriptSet.ClientModule));

        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingManifest_Throws()
    {
        using var dir = new TempDir(writeManifest: false);
        dir.Write("001-a.txt", "A");

        Assert.Throws<InvalidOperationException>(() => SmScriptSet.Load(dir.Path, SmScriptSet.ClientModule));
    }

    private static string[] Chunks(string value) => [.. value.Chunk(32).Select(c => new string(c))];

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sm-scripts-" + Guid.NewGuid().ToString("N"));

        public TempDir(bool writeManifest = true)
        {
            Directory.CreateDirectory(Path);
            if (writeManifest)
            {
                Write("module.json", "{ \"version\": 1, \"sha256\": \"x\" }");
            }
        }

        public void Write(string name, string content) => File.WriteAllText(System.IO.Path.Combine(Path, name), content);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
