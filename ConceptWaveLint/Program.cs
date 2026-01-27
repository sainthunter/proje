using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace ConceptWaveLint;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: ConceptWaveLint <input.xml> [lint-config.json]");
            return 1;
        }

        var inputPath = args[0];
        var configPath = args.Length > 1 ? args[1] : "lint-config.json";
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input XML not found: {inputPath}");
            return 1;
        }

        var lintSettings = LintSettings.Load(configPath);
        ILintRunner lintRunner = EslintRunner.TryCreate(lintSettings) ?? new BasicLintRunner(lintSettings);

        var issues = new List<LintIssue>();
        foreach (var script in XmlScriptExtractor.ReadScripts(inputPath))
        {
            var lintResults = lintRunner.Analyze(script.ScriptBody, script.Name);
            foreach (var issue in lintResults)
            {
                issues.Add(issue with
                {
                    ScriptName = script.Name,
                    ScriptLineOffset = script.ScriptLine,
                    XmlLine = script.XmlLine,
                    XmlPosition = script.XmlPosition
                });
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(issues, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        return 0;
    }
}

internal sealed record ScriptBlock(
    string Name,
    string ScriptBody,
    int ScriptLine,
    int XmlLine,
    int XmlPosition);

internal sealed record LintIssue(
    string ScriptName,
    string Rule,
    string Message,
    int Line,
    int Column,
    int ScriptLineOffset,
    int XmlLine,
    int XmlPosition);

internal static class XmlScriptExtractor
{
    public static IEnumerable<ScriptBlock> ReadScripts(string path)
    {
        var settings = new XmlReaderSettings
        {
            IgnoreComments = false,
            IgnoreWhitespace = false
        };

        using var reader = XmlReader.Create(path, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || !reader.Name.Equals("Script", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)reader;
            using var subtree = reader.ReadSubtree();
            var scriptName = string.Empty;
            var scriptBody = string.Empty;
            var scriptLine = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
            var scriptPos = lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0;

            while (subtree.Read())
            {
                if (subtree.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (subtree.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    scriptName = subtree.ReadElementContentAsString().Trim();
                }
                else if (subtree.Name.Equals("script", StringComparison.OrdinalIgnoreCase))
                {
                    var scriptLineInfo = (IXmlLineInfo)subtree;
                    if (scriptLineInfo.HasLineInfo())
                    {
                        scriptLine = scriptLineInfo.LineNumber;
                        scriptPos = scriptLineInfo.LinePosition;
                    }

                    var raw = subtree.ReadElementContentAsString();
                    scriptBody = WebUtility.HtmlDecode(raw);
                }
            }

            if (!string.IsNullOrWhiteSpace(scriptBody))
            {
                yield return new ScriptBlock(
                    string.IsNullOrWhiteSpace(scriptName) ? "(unnamed)" : scriptName,
                    scriptBody,
                    scriptLine,
                    scriptLine,
                    scriptPos);
            }
        }
    }
}

internal interface ILintRunner
{
    IReadOnlyList<LintIssue> Analyze(string script, string scriptName);
}

internal sealed class BasicLintRunner : ILintRunner
{
    private readonly LintSettings _settings;

    public BasicLintRunner(LintSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<LintIssue> Analyze(string script, string scriptName)
    {
        var issues = new List<LintIssue>();
        if (_settings.Rules.EqeqeqEnabled)
        {
            var lines = script.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("==") && !line.Contains("==="))
                {
                    issues.Add(new LintIssue(
                        scriptName,
                        "eqeqeq",
                        "Use '===' instead of '=='.",
                        i + 1,
                        line.IndexOf("==", StringComparison.Ordinal) + 1,
                        0,
                        0,
                        0));
                }
            }
        }

        return issues;
    }
}

internal sealed class EslintRunner : ILintRunner
{
    private readonly LintSettings _settings;
    private readonly string _eslintPath;

    private EslintRunner(LintSettings settings, string eslintPath)
    {
        _settings = settings;
        _eslintPath = eslintPath;
    }

    public static EslintRunner? TryCreate(LintSettings settings)
    {
        var eslintPath = Environment.GetEnvironmentVariable("ESLINT_PATH");
        if (!string.IsNullOrWhiteSpace(eslintPath))
        {
            return new EslintRunner(settings, eslintPath);
        }

        return null;
    }

    public IReadOnlyList<LintIssue> Analyze(string script, string scriptName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cwlint_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"{scriptName}.js");
        var configPath = Path.Combine(tempDir, ".eslintrc.json");

        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        File.WriteAllText(configPath, JsonSerializer.Serialize(_settings.EslintConfig(), new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        var output = RunEslint(scriptPath, configPath);
        Directory.Delete(tempDir, true);

        return output;
    }

    private IReadOnlyList<LintIssue> RunEslint(string scriptPath, string configPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _eslintPath,
                Arguments = $"--no-eslintrc --config \"{configPath}\" -f json \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
        {
            return new List<LintIssue>
            {
                new(
                    "(eslint)",
                    "eslint-error",
                    $"ESLint failed: {stderr.Trim()}",
                    0,
                    0,
                    0,
                    0,
                    0)
            };
        }

        return ParseEslintJson(stdout);
    }

    private static IReadOnlyList<LintIssue> ParseEslintJson(string json)
    {
        var issues = new List<LintIssue>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return issues;
        }

        using var doc = JsonDocument.Parse(json);
        foreach (var file in doc.RootElement.EnumerateArray())
        {
            foreach (var message in file.GetProperty("messages").EnumerateArray())
            {
                issues.Add(new LintIssue(
                    string.Empty,
                    message.GetProperty("ruleId").GetString() ?? "unknown",
                    message.GetProperty("message").GetString() ?? string.Empty,
                    message.GetProperty("line").GetInt32(),
                    message.GetProperty("column").GetInt32(),
                    0,
                    0,
                    0));
            }
        }

        return issues;
    }
}

internal sealed class LintSettings
{
    public LintRuleSettings Rules { get; init; } = new();

    public static LintSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new LintSettings();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LintSettings>(json) ?? new LintSettings();
    }

    public Dictionary<string, object> EslintConfig()
    {
        return new Dictionary<string, object>
        {
            ["env"] = new Dictionary<string, bool>
            {
                ["browser"] = true,
                ["es2021"] = true
            },
            ["parserOptions"] = new Dictionary<string, object>
            {
                ["ecmaVersion"] = 2021,
                ["sourceType"] = "script"
            },
            ["rules"] = new Dictionary<string, object?>
            {
                ["eqeqeq"] = Rules.EqeqeqEnabled ? "error" : "off",
                ["no-undef"] = Rules.NoUndefEnabled ? "error" : "off",
                ["no-unused-vars"] = Rules.NoUnusedVarsEnabled ? "warn" : "off"
            }
        };
    }
}

internal sealed class LintRuleSettings
{
    public bool EqeqeqEnabled { get; init; } = true;
    public bool NoUndefEnabled { get; init; } = true;
    public bool NoUnusedVarsEnabled { get; init; } = false;
}
