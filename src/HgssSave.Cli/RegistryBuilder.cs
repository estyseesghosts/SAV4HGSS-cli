using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HgssSave.Core;

namespace HgssSave.Cli;

/// <summary>
/// Offline indexer: compiles the pinned <c>pret/pokeheartgold</c> headers into the
/// versioned symbol registry artifact the CLI reads at runtime (design §7).
/// Integer expressions are evaluated by a restricted evaluator over literals,
/// parentheses, arithmetic/bitwise operators and previously defined names only.
/// Anything else is reported as skipped, never guessed.
/// </summary>
internal static class RegistryBuilder
{
    internal sealed record Stats(int Symbols, int Skipped, int Files, string ManifestPath);

    private sealed record Macro(string Name, string Expression, string Source, int Line);

    private static readonly string[] HeaderFiles =
    [
        "flags.h", "vars.h", "maps.h", "items.h", "species.h", "moves.h",
    ];

    internal static Stats Build(string headersDir, string commit, string registryId, string project, string outputPath)
    {
        var constantsDir = Path.Combine(headersDir, "include", "constants");
        var macros = new List<Macro>();
        var filesUsed = new List<string>();
        foreach (var file in HeaderFiles)
        {
            var path = Path.Combine(constantsDir, file);
            if (!File.Exists(path))
                throw HgssException.BadArguments("registry-headers", $"Header not found: {path}");
            macros.AddRange(ParseHeader(path, $"include/constants/{file}"));
            filesUsed.Add($"include/constants/{file}");
        }

        var values = EvaluateAll(macros);
        var entries = Classify(macros, values, out var skipped);

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);
        using (var stream = File.Create(outputPath))
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("schema", SymbolRegistry.SchemaId);
            writer.WriteString("registry_id", registryId);
            writer.WriteStartObject("source");
            writer.WriteString("project", project);
            writer.WriteString("commit", commit);
            writer.WriteStartArray("files");
            foreach (var file in filesUsed)
                writer.WriteStringValue(file);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteStartArray("symbols");
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteString("kind", SymbolKinds.ToJson(entry.Kind));
                writer.WriteNumber("id", entry.Id);
                writer.WriteString("hex", entry.Hex);
                writer.WriteString("source", entry.Source);
                writer.WriteNumber("line", entry.Line);
                writer.WriteBoolean("writable", entry.Writable);
                writer.WriteStartArray("aliases");
                foreach (var alias in entry.Aliases)
                    writer.WriteStringValue(alias);
                writer.WriteEndArray();
                writer.WriteString("confidence", entry.Confidence);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("skipped");
            foreach (var skip in skipped)
            {
                writer.WriteStartObject();
                writer.WriteString("name", skip.Name);
                writer.WriteString("source", skip.Source);
                writer.WriteNumber("line", skip.Line);
                writer.WriteString("reason", skip.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var manifestPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".", "source-manifest.json");
        var counts = entries.GroupBy(e => SymbolKinds.ToJson(e.Kind)).ToDictionary(g => g.Key, g => g.Count());
        using (var manifest = File.Create(manifestPath))
        {
            using var writer = new Utf8JsonWriter(manifest, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("registry", $"{registryId}@{commit}");
            writer.WriteNumber("symbols", entries.Count);
            writer.WriteNumber("skipped", skipped.Count);
            writer.WriteNumber("files", filesUsed.Count);
            writer.WriteStartObject("kinds");
            foreach (var pair in counts.OrderBy(p => p.Key))
                writer.WriteNumber(pair.Key, pair.Value);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return new Stats(entries.Count, skipped.Count, filesUsed.Count, manifestPath);
    }

    private static List<Macro> ParseHeader(string path, string source)
    {
        var macros = new List<Macro>();
        var lines = File.ReadAllLines(path);
        var continued = new StringBuilder();
        var startLine = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]).TrimEnd();
            if (continued.Length == 0)
                startLine = i + 1;
            if (line.EndsWith('\\'))
            {
                continued.Append(line[..^1]);
                continue;
            }

            continued.Append(line);
            var full = continued.ToString().Trim();
            continued.Clear();
            if (!full.StartsWith("#define", StringComparison.Ordinal))
                continue;
            var rest = full["#define".Length..].Trim();
            var nameEnd = rest.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_').Count();
            if (nameEnd == 0)
                continue;
            var name = rest[..nameEnd];
            // Function-like macros glue the parameter list to the name; value
            // expressions like "(VAR_BASE + ...)" always have a space first.
            if (nameEnd < rest.Length && rest[nameEnd] == '(')
                continue;
            var value = rest[nameEnd..].Trim();
            if (value.Length == 0)
                continue;
            macros.Add(new Macro(name, value, source, startLine));
        }

        return macros;
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i + 1 < line.Length; i++)
        {
            if (line[i] == '"')
                inString = !inString;
            if (!inString && line[i] == '/' && line[i + 1] == '/')
                return line[..i];
        }

        return line;
    }

    private static Dictionary<string, long> EvaluateAll(List<Macro> macros)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        var pending = new List<Macro>(macros);
        var progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            var rest = new List<Macro>(pending.Count);
            foreach (var macro in pending)
            {
                if (Evaluator.TryEvaluate(macro.Expression, values, out var value))
                {
                    values[macro.Name] = value;
                    progress = true;
                }
                else
                {
                    rest.Add(macro);
                }
            }

            pending = rest;
        }

        return values;
    }

    private static List<SymbolEntry> Classify(List<Macro> macros, Dictionary<string, long> values, out List<SkippedMacro> skipped)
    {
        skipped = [];
        var resolved = macros
            .Where(m => values.TryGetValue(m.Name, out _))
            .Select(m => (Macro: m, Value: values[m.Name]))
            .Where(t => t.Value is >= int.MinValue and <= int.MaxValue)
            .ToList();
        var unresolved = macros.Where(m => !values.ContainsKey(m.Name)).ToList();
        foreach (var macro in unresolved)
            skipped.Add(new SkippedMacro(macro.Name, macro.Source, macro.Line, "unresolved expression; not guessed"));

        values.TryGetValue("NUM_FLAGS", out var numFlags);
        values.TryGetValue("VAR_BASE", out var varBase);
        values.TryGetValue("NUM_VARS", out var numVars);
        values.TryGetValue("TEMP_VAR_BASE", out var tempBase);
        values.TryGetValue("NUM_TEMP_VARS", out var numTemp);
        values.TryGetValue("VAR_OBJ_GFX_BASE", out var objBase);
        values.TryGetValue("NUM_OBJ_GFX_VARS", out var numObj);
        values.TryGetValue("SPECIAL_VAR_BASE", out var specialBase);
        if (numFlags == 0 || varBase == 0 || numVars == 0)
            throw HgssException.Internal("registry-bounds", "Pinned headers are missing NUM_FLAGS, VAR_BASE or NUM_VARS; refusing to classify.");

        var entries = new List<SymbolEntry>();
        foreach (var (macro, value) in resolved)
        {
            var id = (int)value;
            var (kind, writable, note) = ClassifyOne(macro, id, (int)numFlags,
                (int)varBase, (int)numVars, (int)tempBase, (int)numTemp, (int)objBase, (int)numObj, (int)specialBase);
            if (kind is null)
            {
                skipped.Add(new SkippedMacro(macro.Name, macro.Source, macro.Line, note ?? "unclassified"));
                continue;
            }

            entries.Add(new SymbolEntry(macro.Name, kind.Value, id, $"0x{id:X}", macro.Source, macro.Line, writable, [], "source-defined"));
        }

        AttachAliases(entries);
        return entries
            .OrderBy(e => KindOrder(e.Kind))
            .ThenBy(e => e.Id)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static (SymbolKind? Kind, bool Writable, string? Note) ClassifyOne(
        Macro macro, int id, int numFlags, int varBase, int numVars,
        int tempBase, int numTemp, int objBase, int numObj, int specialBase)
    {
        var file = macro.Source;
        var name = macro.Name;
        if (file.EndsWith("flags.h", StringComparison.Ordinal))
        {
            if (name.StartsWith("FLAG_ACTION_", StringComparison.Ordinal) || name == "FLAG_NOTHING")
                return (SymbolKind.Unknown, false, null);
            if (name is "NUM_FLAGS" or "MAPTEMP_FLAG_BASE" or "NUM_MAPTEMP_FLAGS" or "HIDDEN_ITEMS_FLAG_BASE")
                return (SymbolKind.RangeConstant, false, null);
            if (name.StartsWith("FLAG_MAPTEMP_", StringComparison.Ordinal) || name.StartsWith("FLAG_TEMP_", StringComparison.Ordinal))
                return (SymbolKind.TemporaryFlag, false, null);
            if (!name.StartsWith("FLAG_", StringComparison.Ordinal))
                return (null, false, "not a flag, action, or range constant");

            // Anything at or above the persistent count is not in the save flag array.
            return id >= 0 && id < numFlags
                ? (SymbolKind.PersistentFlag, true, null)
                : (SymbolKind.Unknown, false, null);
        }

        if (file.EndsWith("vars.h", StringComparison.Ordinal))
        {
            if (name is "VAR_BASE" or "TEMP_VAR_BASE" or "NUM_TEMP_VARS" or "VAR_OBJ_GFX_BASE"
                or "NUM_OBJ_GFX_VARS" or "NUM_VARS" or "SPECIAL_VAR_BASE" or "NUM_SPECIAL_VARS"
                or "VARS_START" or "VARS_END" or "SPECIAL_VARS_START" or "SPECIAL_VARS_END")
                return (SymbolKind.RangeConstant, false, null);
            var saveEnd = varBase + numVars;
            var tempEnd = tempBase + numTemp;
            var objEnd = objBase + numObj;
            if (id >= tempBase && id < tempEnd)
                return (SymbolKind.TemporaryVar, false, null);
            if (id >= objBase && id < objEnd)
                return (SymbolKind.TemporaryVar, false, null);
            if (id >= specialBase)
                return (SymbolKind.SpecialRuntimeVar, false, null);
            if (id >= varBase && id < saveEnd)
                return (SymbolKind.SaveWork, true, null);
            return (SymbolKind.Unknown, false, null);
        }

        if (file.EndsWith("maps.h", StringComparison.Ordinal))
            return name.StartsWith("MAP_", StringComparison.Ordinal)
                ? (SymbolKind.Map, false, null)
                : (null, false, "not a map id");

        if (file.EndsWith("items.h", StringComparison.Ordinal))
        {
            if (name.StartsWith("ITEM_", StringComparison.Ordinal))
                return id >= 0 ? (SymbolKind.Item, false, null) : (SymbolKind.Unknown, false, null);
            if (name.StartsWith("POCKET_", StringComparison.Ordinal) || name.StartsWith("NUM_BAG_", StringComparison.Ordinal)
                || name.StartsWith("BAG_", StringComparison.Ordinal) || name.StartsWith("ITEMNARC_", StringComparison.Ordinal)
                || name.StartsWith("BTLPOCKET", StringComparison.Ordinal) || name.StartsWith("BPOCKET_", StringComparison.Ordinal))
                return (SymbolKind.RangeConstant, false, null);
            return (null, false, "not an item or pocket constant");
        }

        if (file.EndsWith("species.h", StringComparison.Ordinal))
            return name.StartsWith("SPECIES_", StringComparison.Ordinal)
                ? (SymbolKind.Species, false, null)
                : (null, false, "not a species id");

        if (file.EndsWith("moves.h", StringComparison.Ordinal))
            return name.StartsWith("MOVE_", StringComparison.Ordinal)
                ? (SymbolKind.Move, false, null)
                : (null, false, "not a move id");

        return (null, false, "unexpected header");
    }

    private static void AttachAliases(List<SymbolEntry> entries)
    {
        var writableBuckets = new HashSet<SymbolKind>
        {
            SymbolKind.PersistentFlag, SymbolKind.TemporaryFlag, SymbolKind.SaveWork,
            SymbolKind.TemporaryVar, SymbolKind.SpecialRuntimeVar,
            SymbolKind.Map, SymbolKind.Item, SymbolKind.Species, SymbolKind.Move,
        };
        var groups = entries
            .Where(e => writableBuckets.Contains(e.Kind))
            .GroupBy(e => (e.Kind, e.Id))
            .Where(g => g.Count() > 1);
        var aliasMap = new Dictionary<SymbolEntry, List<string>>();
        foreach (var group in groups)
        {
            var names = group.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            foreach (var entry in group)
                aliasMap[entry] = names.Where(n => n != entry.Name).ToList();
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (aliasMap.TryGetValue(entries[i], out var aliases))
                entries[i] = entries[i] with { Aliases = aliases };
        }
    }

    private static int KindOrder(SymbolKind kind) => kind switch
    {
        SymbolKind.PersistentFlag => 0,
        SymbolKind.TemporaryFlag => 1,
        SymbolKind.SaveWork => 2,
        SymbolKind.TemporaryVar => 3,
        SymbolKind.SpecialRuntimeVar => 4,
        SymbolKind.Map => 5,
        SymbolKind.Item => 6,
        SymbolKind.Species => 7,
        SymbolKind.Move => 8,
        SymbolKind.RangeConstant => 9,
        SymbolKind.Alias => 10,
        _ => 11,
    };

    /// <summary>Restricted integer expression evaluator: literals, names, parens, unary and binary operators.</summary>
    private static class Evaluator
    {
        internal static bool TryEvaluate(string expression, Dictionary<string, long> names, out long value)
        {
            value = 0;
            try
            {
                var parser = new Parser(expression, names);
                value = parser.ParseFull();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class Parser(string text, Dictionary<string, long> names)
        {
            private int _pos;

            internal long ParseFull()
            {
                var value = ParseOr();
                SkipSpace();
                if (_pos != text.Length)
                    throw new InvalidOperationException("trailing tokens");
                return value;
            }

            private long ParseOr()
            {
                var value = ParseXor();
                while (Take('|'))
                    value |= ParseXor();
                return value;
            }

            private long ParseXor()
            {
                var value = ParseAnd();
                while (Take('^'))
                    value ^= ParseAnd();
                return value;
            }

            private long ParseAnd()
            {
                var value = ParseShift();
                while (Take('&'))
                    value &= ParseShift();
                return value;
            }

            private long ParseShift()
            {
                var value = ParseAdd();
                while (true)
                {
                    if (Take('<', '<'))
                        value <<= (int)ParseAdd();
                    else if (Take('>', '>'))
                        value >>= (int)ParseAdd();
                    else
                        return value;
                }
            }

            private long ParseAdd()
            {
                var value = ParseMul();
                while (true)
                {
                    SkipSpace();
                    if (_pos < text.Length && (text[_pos] == '+' || text[_pos] == '-'))
                    {
                        var op = text[_pos++];
                        var rhs = ParseMul();
                        value = op == '+' ? value + rhs : value - rhs;
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private long ParseMul()
            {
                var value = ParseUnary();
                while (true)
                {
                    SkipSpace();
                    if (_pos < text.Length && (text[_pos] == '*' || text[_pos] == '/' || text[_pos] == '%'))
                    {
                        var op = text[_pos++];
                        var rhs = ParseUnary();
                        value = op switch
                        {
                            '*' => value * rhs,
                            '/' => rhs == 0 ? throw new InvalidOperationException("divide by zero") : value / rhs,
                            _ => rhs == 0 ? throw new InvalidOperationException("divide by zero") : value % rhs,
                        };
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private long ParseUnary()
            {
                SkipSpace();
                if (_pos < text.Length && (text[_pos] == '-' || text[_pos] == '+' || text[_pos] == '~'))
                {
                    var op = text[_pos++];
                    var operand = ParseUnary();
                    return op switch
                    {
                        '-' => -operand,
                        '+' => operand,
                        _ => ~operand,
                    };
                }

                return ParsePrimary();
            }

            private long ParsePrimary()
            {
                SkipSpace();
                if (_pos < text.Length && text[_pos] == '(')
                {
                    _pos++;
                    var value = ParseOr();
                    SkipSpace();
                    if (_pos >= text.Length || text[_pos] != ')')
                        throw new InvalidOperationException("missing paren");
                    _pos++;
                    return value;
                }

                if (_pos < text.Length && (char.IsLetter(text[_pos]) || text[_pos] == '_'))
                {
                    var start = _pos;
                    while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_'))
                        _pos++;
                    var name = text[start.._pos];
                    if (names.TryGetValue(name, out var value))
                        return value;
                    throw new InvalidOperationException($"unknown name {name}");
                }

                if (_pos < text.Length && char.IsDigit(text[_pos]))
                    return ParseNumber();
                throw new InvalidOperationException("expected value");
            }

            private long ParseNumber()
            {
                var start = _pos;
                if (text[_pos] == '0' && _pos + 1 < text.Length && (text[_pos + 1] == 'x' || text[_pos + 1] == 'X'))
                {
                    _pos += 2;
                    var hexStart = _pos;
                    while (_pos < text.Length && Uri.IsHexDigit(text[_pos]))
                        _pos++;
                    if (_pos == hexStart)
                        throw new InvalidOperationException("bad hex");
                    SkipSuffix();
                    return Convert.ToInt64(text[hexStart.._pos], 16);
                }

                while (_pos < text.Length && char.IsDigit(text[_pos]))
                    _pos++;
                var digits = text[start.._pos];
                SkipSuffix();
                return digits.Length > 1 && digits[0] == '0'
                    ? Convert.ToInt64(digits, 8)
                    : long.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
            }

            private void SkipSuffix()
            {
                while (_pos < text.Length && (text[_pos] is 'u' or 'U' or 'l' or 'L'))
                    _pos++;
            }

            private void SkipSpace()
            {
                while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
                    _pos++;
            }

            private bool Take(char first, char? second = null)
            {
                SkipSpace();
                if (_pos < text.Length && text[_pos] == first && (second is null || (_pos + 1 < text.Length && text[_pos + 1] == second)))
                {
                    _pos += second is null ? 1 : 2;
                    // A single & or | must not swallow the second char of && or ||, which are unsupported.
                    if (second is null && (first is '&' or '|') && _pos < text.Length && text[_pos] == first)
                        throw new InvalidOperationException("logical operators are unsupported");
                    return true;
                }

                return false;
            }
        }
    }
}
