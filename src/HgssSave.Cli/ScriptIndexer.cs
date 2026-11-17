using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HgssSave.Core;

namespace HgssSave.Cli;

/// <summary>
/// Offline script indexer: scans pokeheartgold field scripts for symbolic
/// flag/variable/item references (design §7). Records reads vs writes with
/// file:line evidence instead of claiming semantic comprehension.
/// </summary>
internal static class ScriptIndexer
{
    internal sealed record Stats(int Files, int Refs, int Symbols);

    private static readonly Regex CommandArg = new(
        @"^\s*(SetFlag|ClearFlag|GoToIfSet|GoToIfUnset|SetVar|Compare|GetGameVersion|GiveItem|TakeItem|HasItem|GetItemCount|CheckItem)\s+([A-Za-z_0-9]+)",
        RegexOptions.Compiled);

    private static readonly Regex Label = new(
        @"^([A-Za-z_][A-Za-z_0-9]*):\s*$",
        RegexOptions.Compiled);

    private static string AccessFor(string command, string symbol) => command switch
    {
        "SetFlag" or "ClearFlag" => symbol.StartsWith("FLAG_", StringComparison.Ordinal) ? ScriptAccess.Write : ScriptAccess.Marker,
        "GoToIfSet" or "GoToIfUnset" => ScriptAccess.Read,
        "SetVar" or "GetGameVersion" => symbol.StartsWith("VAR_", StringComparison.Ordinal) ? ScriptAccess.Write : ScriptAccess.Marker,
        "Compare" => ScriptAccess.Read,
        "GiveItem" or "TakeItem" => ScriptAccess.Write,
        "HasItem" or "GetItemCount" or "CheckItem" => ScriptAccess.ItemCheck,
        _ => ScriptAccess.Marker,
    };

    private static string ConfidenceFor(string command) => command switch
    {
        "SetFlag" or "ClearFlag" or "SetVar" or "GoToIfSet" or "GoToIfUnset"
            or "Compare" or "GiveItem" or "TakeItem" or "HasItem" => "direct-read",
        _ => "unknown",
    };

    internal static Stats Build(string scriptsDir, string commit, string project, string outputPath)
    {
        var files = Directory.GetFiles(scriptsDir, "*.s", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0)
            throw HgssException.BadArguments("script-dir", $"No .s files found under {scriptsDir}.");

        var refs = new List<Dictionary<string, object?>>();
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            string? context = null;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var label = Label.Match(lines[i]);
                if (label.Success)
                {
                    context = label.Groups[1].Value;
                    continue;
                }

                var m = CommandArg.Match(lines[i]);
                if (!m.Success)
                    continue;
                var command = m.Groups[1].Value;
                var symbol = m.Groups[2].Value;
                if (!symbol.StartsWith("FLAG_", StringComparison.Ordinal)
                    && !symbol.StartsWith("VAR_", StringComparison.Ordinal)
                    && !symbol.StartsWith("ITEM_", StringComparison.Ordinal))
                    continue;

                refs.Add(new Dictionary<string, object?>
                {
                    ["symbol"] = symbol,
                    ["access"] = AccessFor(command, symbol),
                    ["command"] = command,
                    ["file"] = name,
                    ["line"] = i + 1,
                    ["context"] = context,
                    ["confidence"] = ConfidenceFor(command),
                });
                symbols.Add(symbol);
            }
        }

        refs = refs.OrderBy(r => (string)r["symbol"]!).ThenBy(r => (string)r["file"]!).ThenBy(r => (int)r["line"]!).ToList();

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        using (var stream = File.Create(outputPath))
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("schema", "hgss-save.script-refs/v1");
            writer.WriteStartObject("source");
            writer.WriteString("project", project);
            writer.WriteString("commit", commit);
            writer.WriteString("scripts_dir", "files/fielddata/script/scr_seq");
            writer.WriteNumber("files_scanned", files.Count);
            writer.WriteEndObject();
            writer.WriteNumber("ref_count", refs.Count);
            writer.WriteNumber("symbol_count", symbols.Count);
            writer.WriteStartArray("refs");
            foreach (var r in refs)
            {
                writer.WriteStartObject();
                writer.WriteString("symbol", (string)r["symbol"]!);
                writer.WriteString("access", (string)r["access"]!);
                writer.WriteString("command", (string)r["command"]!);
                writer.WriteString("file", (string)r["file"]!);
                writer.WriteNumber("line", (int)r["line"]!);
                if (r["context"] is string ctx && ctx.Length > 0)
                    writer.WriteString("context", ctx);
                else
                    writer.WriteNull("context");
                writer.WriteString("confidence", (string)r["confidence"]!);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return new Stats(files.Count, refs.Count, symbols.Count);
    }

    internal static IReadOnlyList<ScriptRef> Query(string indexPath, string symbol)
    {
        var bytes = File.ReadAllBytes(indexPath);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        if (root.TryGetProperty("schema", out var schema)
            && schema.GetString() != "hgss-save.script-refs/v1")
            throw HgssException.BadArguments("script-refs-schema", $"Script index {indexPath} has unexpected schema.");
        var results = new List<ScriptRef>();
        if (!root.TryGetProperty("refs", out var refs) || refs.ValueKind != JsonValueKind.Array)
            return results;
        foreach (var item in refs.EnumerateArray())
        {
            var name = item.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
            if (!string.Equals(name, symbol, StringComparison.OrdinalIgnoreCase))
                continue;
            results.Add(new ScriptRef(
                name,
                item.TryGetProperty("access", out var a) ? a.GetString() ?? "" : "",
                item.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "",
                item.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "",
                item.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0,
                item.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.String ? ctx.GetString() : null,
                item.TryGetProperty("confidence", out var conf) ? conf.GetString() ?? "" : ""));
        }

        return results.OrderBy(r => r.File, StringComparer.Ordinal).ThenBy(r => r.Line).ToList();
    }
}
