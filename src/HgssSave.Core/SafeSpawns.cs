using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HgssSave.Core;

/// <summary>A verified safe spawn: a map tile a real played save stood on
/// (fixtures/safe-spawns.json). Location edits are allowlisted to these.</summary>
public sealed record SafeSpawn(
    int MapId,
    string? MapSymbol,
    int X,
    int Y,
    int Z,
    string? Note);

public static class SafeSpawnRegistry
{
    public const string SchemaId = "hgss-save.safe-spawns/v1";

    public static IReadOnlyList<SafeSpawn> LoadDefault(string? explicitPath = null)
    {
        var path = explicitPath
            ?? Environment.GetEnvironmentVariable("HGSS_SAFE_SPAWNS")
            ?? FindDefault();
        if (path is null || !File.Exists(path))
            throw HgssException.BadArguments("spawns-missing",
                $"Safe-spawn registry not found ({path ?? "(null)"}). Pass --safe-spawns PATH or set HGSS_SAFE_SPAWNS; location edits are refused without it.");
        return Load(path);
    }

    private static string? FindDefault()
    {
        var candidates = new List<string?>();
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null)
        {
            candidates.Add(Path.Combine(exeDir, "safe-spawns.json"));
            candidates.Add(Path.Combine(exeDir, "fixtures", "safe-spawns.json"));
        }

        var cwd = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(cwd, "safe-spawns.json"));
        candidates.Add(Path.Combine(cwd, "fixtures", "safe-spawns.json"));
        return candidates.FirstOrDefault(c => c is not null && File.Exists(c));
    }

    public static IReadOnlyList<SafeSpawn> Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw HgssException.BadArguments("spawns-io", $"Cannot read safe-spawn registry '{path}': {e.Message}");
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (!root.TryGetProperty("spawns", out var spawns) || spawns.ValueKind != JsonValueKind.Array)
                throw HgssException.BadArguments("spawns-shape", $"Safe-spawn registry '{path}' must define a 'spawns' array.");
            var list = new List<SafeSpawn>();
            foreach (var item in spawns.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (!item.TryGetProperty("map_id", out var m) || m.ValueKind != JsonValueKind.Number)
                    continue;
                list.Add(new SafeSpawn(
                    m.GetInt32(),
                    item.TryGetProperty("map_symbol", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null,
                    item.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : 0,
                    item.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : 0,
                    item.TryGetProperty("z", out var z) && z.ValueKind == JsonValueKind.Number ? z.GetInt32() : 0,
                    item.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null));
            }

            return list;
        }
        catch (JsonException e)
        {
            throw HgssException.BadArguments("spawns-json", $"Safe-spawn registry '{path}' is not valid JSON: {e.Message}");
        }
    }

    public static bool Contains(IReadOnlyList<SafeSpawn> spawns, int mapId, int x, int y, int z) =>
        spawns.Any(s => s.MapId == mapId && s.X == x && s.Y == y && s.Z == z);
}
