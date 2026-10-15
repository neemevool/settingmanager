using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace VTMF.SQLInstaller.Services;

public class SqlScriptParser
{
    private readonly ILogger<SqlScriptParser> _logger;
    private static readonly Regex VersionRegex = new(@"--\s*Version\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SqlScriptParser(ILogger<SqlScriptParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts the version number from the first line of a SQL script.
    /// Expected format: -- Version: 1
    /// </summary>
    public int? ExtractVersion(string scriptContent)
    {
        if (string.IsNullOrWhiteSpace(scriptContent))
        {
            return null;
        }

        var firstLine = scriptContent.Split('\n', 2)[0];
        var match = VersionRegex.Match(firstLine);

        if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
        {
            return version;
        }

        return null;
    }

    /// <summary>
    /// Splits a SQL script into batches separated by GO statements.
    /// GO statements are removed from the output.
    /// </summary>
    public IEnumerable<string> SplitIntoBatches(string scriptContent)
    {
        if (string.IsNullOrWhiteSpace(scriptContent))
        {
            yield break;
        }

        // Split by GO statements (case-insensitive, must be on its own line)
        var lines = scriptContent.Split('\n');
        var currentBatch = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Check if this line is a GO statement
            if (trimmedLine.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                // Yield the current batch if it has content
                var batch = string.Join('\n', currentBatch).Trim();
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    yield return batch;
                }
                currentBatch.Clear();
            }
            else
            {
                currentBatch.Add(line);
            }
        }

        // Yield any remaining content
        var finalBatch = string.Join('\n', currentBatch).Trim();
        if (!string.IsNullOrWhiteSpace(finalBatch))
        {
            yield return finalBatch;
        }
    }
}
