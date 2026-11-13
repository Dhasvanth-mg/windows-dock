using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Syntax;

namespace WindowsDock;

internal static class AgentDefaults
{
    public static (string? Model, string? Effort) Read(AgentProvider provider)
    {
        var path = Config(provider);
        if (!File.Exists(path))
            return (null, null);
        var contents = File.ReadAllText(path);
        if (provider == AgentProvider.Claude)
        {
            var json = AgentHookSetup.ParseConfig(contents);
            return (json["model"]?.GetValue<string>(), json["effortLevel"]?.GetValue<string>());
        }

        var toml = Toml.ToModel(contents);
        return (toml.TryGetValue("model", out var model) ? model as string : null,
            toml.TryGetValue("model_reasoning_effort", out var effort) ? effort as string : null);
    }

    public static void Save(AgentProvider provider, string model, string effort)
    {
        model = model.Trim();
        if (model.Length > 128 || model.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                character is not ('-' or '_' or '.' or '/' or ':' or '[' or ']')))
            throw new InvalidOperationException("Enter a model ID or alias from your provider.");

        var path = Config(provider);
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        string result;
        if (provider == AgentProvider.Claude)
        {
            var config = AgentHookSetup.ParseConfig(original);
            if (model.Length > 0)
                config["model"] = model;
            if (effort.Length > 0)
                config["effortLevel"] = effort;
            result = config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        }
        else
        {
            result = original ?? "";
            if (model.Length > 0)
                result = SetRootString(result, "model", model);
            if (effort.Length > 0)
                result = SetRootString(result, "model_reasoning_effort", effort);
            _ = Toml.ToModel(result);
        }
        if (result != original)
            AgentHookSetup.SaveConfig(path, original, result);
    }

    private static string SetRootString(string contents, string key, string value)
    {
        var parsed = Toml.Parse(contents);
        if (parsed.HasErrors)
            throw new InvalidOperationException("Codex config has TOML errors.");
        var text = JsonSerializer.Serialize(value);
        foreach (var item in parsed.KeyValues)
        {
            if (item.Key is not { Key: { } name } syntax ||
                syntax.DotKeys?.Any() == true || name.ToString().Trim().Trim('"', '\'') != key)
                continue;
            if (item.Value is not StringValueSyntax { Token: { Text: { } tokenText } token })
                throw new InvalidOperationException($"The existing {key} setting is not a string.");
            var start = token.Span.Start.Offset;
            return contents[..start] + text + contents[(start + tokenText.Length)..];
        }
        var newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return key + " = " + text + newline + contents;
    }

    private static string Config(AgentProvider provider) => provider == AgentProvider.Claude
        ? AgentSessions.ConfigPath(provider) : Path.Combine(AgentSessions.CodexHome, "config.toml");
}
