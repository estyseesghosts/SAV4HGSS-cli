using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HgssSave.Core;

public sealed record SymbolEntry(
    string Name,
    SymbolKind Kind,
    int Id,
    string Hex,
    string Source,
    int Line,
    bool Writable,
    IReadOnlyList<string> Aliases,
    string Confidence);

public sealed record SkippedMacro(string Name, string Source, int Line, string Reason);

public sealed record SymbolSource(string Project, string Commit, IReadOnlyList<string> Files);

/// <summary>
/// Versioned symbol registry compiled from a pinned <c>pret/pokeheartgold</c> checkout
/// (design §7). Runtime code never parses headers; it only reads this artifact.
/// </summary>
public sealed class SymbolRegistry
{
    public const string SchemaId = "hgss-save.symbols/v1";

    public string RegistryId { get; }

    public SymbolSource Source { get; }

    public IReadOnlyList<SymbolEntry> Entries { get; }

    public IReadOnlyList<SkippedMacro> Skipped { get; }

    /// <summary>Registry revision string used in reports and patch documents: <c>id@commit</c>.</summary>
    public string Revision { get; }

    /// <summary>Degraded registry used when no registry file is available: numeric ids still resolve.</summary>
    public static SymbolRegistry Empty { get; } = new SymbolRegistry(
        "not-loaded", new SymbolSource("not-loaded", "not-loaded", []), [], [], "not-loaded");

    private readonly Dictionary<string, List<SymbolEntry>> _byName;
    private readonly Dictionary<(SymbolKind, int), SymbolEntry> _byId;

    private SymbolRegistry(string registryId, SymbolSource source, List<SymbolEntry> entries, List<SkippedMacro> skipped, string? revision = null)
    {
        RegistryId = registryId;
        Source = source;
        Entries = entries;
        Skipped = skipped;
        Revision = revision ?? $"{registryId}@{source.Commit}";

        _byName = new Dictionary<string, List<SymbolEntry>>(StringComparer.OrdinalIgnoreCase);
        _byId = new Dictionary<(SymbolKind, int), SymbolEntry>();
        foreach (var entry in entries)
        {
            if (!_byName.TryGetValue(entry.Name, out var list))
            {
                list = [];
                _byName[entry.Name] = list;
            }

            list.Add(entry);
            _byId.TryAdd((entry.Kind, entry.Id), entry);
        }
    }

    public static SymbolRegistry Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw HgssException.BadArguments("registry-missing", $"Symbol registry not found: {path}");
        }

        return Parse(bytes, path);
    }

    /// <summary>Searches the default registry location next to the CLI binary.</summary>
    public static SymbolRegistry LoadDefault()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? Directory.GetCurrentDirectory();
        var path = Path.Combine(dir, "hgss-us.symbols.json");
        return Load(path);
    }

    public static SymbolRegistry Parse(byte[] bytes, string origin)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException e)
        {
            throw HgssException.BadArguments("registry-json", $"Symbol registry is not valid JSON ({origin}): {e.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw HgssException.BadArguments("registry-shape", $"Symbol registry root must be an object ({origin}).");

            var schema = GetString(root, "schema") ?? throw HgssException.BadArguments("registry-shape", $"Symbol registry is missing 'schema' ({origin}).");
            if (schema != SchemaId)
                throw HgssException.BadArguments("registry-schema", $"Symbol registry schema '{schema}' is not supported (expected {SchemaId}).");

            var registryId = GetString(root, "registry_id") ?? throw HgssException.BadArguments("registry-shape", $"Symbol registry is missing 'registry_id' ({origin}).");
            var sourceObj = root.TryGetProperty("source", out var s) ? s : default;
            if (sourceObj.ValueKind != JsonValueKind.Object)
                throw HgssException.BadArguments("registry-shape", $"Symbol registry is missing 'source' ({origin}).");

            var project = GetString(sourceObj, "project") ?? "unknown";
            var commit = GetString(sourceObj, "commit") ?? throw HgssException.BadArguments("registry-shape", $"Symbol registry 'source' is missing 'commit' ({origin}).");
            var files = sourceObj.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array
                ? f.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                : [];

            var entries = new List<SymbolEntry>();
            if (root.TryGetProperty("symbols", out var symbols) && symbols.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in symbols.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        throw HgssException.BadArguments("registry-shape", $"Symbol registry contains a non-object symbol entry ({origin}).");

                    var name = GetString(item, "name") ?? throw HgssException.BadArguments("registry-shape", $"Symbol entry is missing 'name' ({origin}).");
                    var kindText = GetString(item, "kind") ?? throw HgssException.BadArguments("registry-shape", $"Symbol '{name}' is missing 'kind' ({origin}).");
                    if (!item.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
                        throw HgssException.BadArguments("registry-shape", $"Symbol '{name}' is missing numeric 'id' ({origin}).");

                    var id = idElement.GetInt32();
                    var hex = GetString(item, "hex") ?? $"0x{id:X}";
                    var src = GetString(item, "source") ?? "";
                    var line = item.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0;
                    var writable = item.TryGetProperty("writable", out var w) && w.ValueKind == JsonValueKind.True;
                    var confidence = GetString(item, "confidence") ?? "source-defined";
                    var aliases = item.TryGetProperty("aliases", out var a) && a.ValueKind == JsonValueKind.Array
                        ? a.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                        : [];

                    entries.Add(new SymbolEntry(name, SymbolKinds.FromJson(kindText), id, hex, src, line, writable, aliases, confidence));
                }
            }

            var skipped = new List<SkippedMacro>();
            if (root.TryGetProperty("skipped", out var skipEl) && skipEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in skipEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var name = GetString(item, "name") ?? "";
                    var src = GetString(item, "source") ?? "";
                    var line = item.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0;
                    var reason = GetString(item, "reason") ?? "";
                    skipped.Add(new SkippedMacro(name, src, line, reason));
                }
            }

            return new SymbolRegistry(registryId, new SymbolSource(project, commit, files), entries, skipped);
        }
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public SymbolLookupResult Lookup(string nameOrNumber)
    {
        if (string.IsNullOrWhiteSpace(nameOrNumber))
            return SymbolLookupResult.NotFound(nameOrNumber);

        if (TryParseNumber(nameOrNumber, out var number))
            return LookupNumber(number);

        if (_byName.TryGetValue(nameOrNumber, out var list))
        {
            if (list.Count == 1)
                return SymbolLookupResult.Found(list[0]);

            // Same name defined in more than one category (e.g. a flag and a var sharing a label).
            return SymbolLookupResult.Ambiguous(nameOrNumber, list);
        }

        return SymbolLookupResult.NotFound(nameOrNumber);
    }

    /// <summary>Resolves a numeric id inside a specific category, when the registry knows it.</summary>
    public SymbolLookupResult LookupNumber(int number, SymbolKind? kind = null)
    {
        if (kind is not null && _byId.TryGetValue((kind.Value, number), out var direct))
            return SymbolLookupResult.Found(direct);

        var matches = Entries.Where(e => e.Id == number).ToList();
        return matches.Count switch
        {
            0 => SymbolLookupResult.Numeric(number),
            1 => SymbolLookupResult.Found(matches[0]),
            _ => SymbolLookupResult.Ambiguous(number.ToString(), matches),
        };
    }

    /// <summary>Finds the symbol for a field reference that must have a specific kind.</summary>
    public SymbolLookupResult LookupAs(string nameOrNumber, SymbolKind kind)
    {
        if (string.IsNullOrWhiteSpace(nameOrNumber))
            return SymbolLookupResult.NotFound(nameOrNumber);

        if (TryParseNumber(nameOrNumber, out var number))
        {
            if (_byId.TryGetValue((kind, number), out var direct))
                return SymbolLookupResult.Found(direct);
            return SymbolLookupResult.Numeric(number);
        }

        if (_byName.TryGetValue(nameOrNumber, out var list))
        {
            var matching = list.Where(e => e.Kind == kind).ToList();
            if (matching.Count == 1)
                return SymbolLookupResult.Found(matching[0]);
            if (matching.Count > 1)
                return SymbolLookupResult.Ambiguous(nameOrNumber, matching);
            return SymbolLookupResult.WrongKind(nameOrNumber, kind, list);
        }

        return SymbolLookupResult.NotFound(nameOrNumber);
    }

    public IReadOnlyList<SymbolEntry> Search(string query, IReadOnlyList<SymbolKind>? kinds, int limit)
    {
        var needle = query.Trim();
        var results = Entries
            .Where(e => kinds is null || kinds.Contains(e.Kind))
            .Where(e => e.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) || e.Aliases.Any(a => a.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.Id)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))
            .ToList();
        return results;
    }

    /// <summary>Best-effort name for a numeric id in a category; null when the registry has no entry.</summary>
    public string? NameFor(SymbolKind kind, int id) =>
        _byId.TryGetValue((kind, id), out var entry) ? entry.Name : null;

    /// <summary>
    /// Name for a variable slot id. Both kinds share one address space: <c>SaveVarsFlags.vars[]</c> covers
    /// 0x4000..0x416F and the temp window is its first <c>NUM_TEMP_VARS</c> entries (F23,
    /// <c>include/constants/vars.h</c> TEMP_VAR_BASE/NUM_TEMP_VARS), so a temp var is readable by name.
    /// </summary>
    public string? VarNameFor(int id) =>
        NameFor(SymbolKind.SaveWork, id) ?? NameFor(SymbolKind.TemporaryVar, id);

    public static bool TryParseNumber(string text, out int number)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && text.Length > 2
            && int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hex))
        {
            number = hex;
            return true;
        }

        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number);
    }
}

public enum SymbolResolution
{
    Resolved,
    NumericUnreviewed,
    NotFound,
    Ambiguous,
    WrongKind,
}

public readonly record struct SymbolLookupResult(
    SymbolResolution Resolution,
    SymbolEntry? Entry,
    string Reference,
    IReadOnlyList<SymbolEntry> Candidates)
{
    public bool IsResolved => Resolution == SymbolResolution.Resolved;

    public static SymbolLookupResult Found(SymbolEntry entry) =>
        new(SymbolResolution.Resolved, entry, entry.Name, [entry]);

    public static SymbolLookupResult Numeric(int number) =>
        new(SymbolResolution.NumericUnreviewed, null, number.ToString(), []);

    public static SymbolLookupResult NotFound(string reference) =>
        new(SymbolResolution.NotFound, null, reference, []);

    public static SymbolLookupResult Ambiguous(string reference, IReadOnlyList<SymbolEntry> candidates) =>
        new(SymbolResolution.Ambiguous, null, reference, candidates);

    public static SymbolLookupResult WrongKind(string reference, SymbolKind kind, IReadOnlyList<SymbolEntry> candidates) =>
        new(SymbolResolution.WrongKind, null, reference, candidates);
}
