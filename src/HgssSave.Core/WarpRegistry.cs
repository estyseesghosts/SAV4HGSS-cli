using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HgssSave.Core;

/// <summary>One warp event of a map, as listed in the decomp zone-event files.</summary>
public sealed record WarpTarget(int MapId, string? MapSymbol, int WarpIndex, int X, int Y, string? Destination);

/// <summary>
/// The per-map warp tables (registry/hgss-us.warp-spawns.json, built by tools/zone_spawn_index.py
/// from pokeheartgold zone_event JSON). <c>location.warp</c> accepts only (map, warp) pairs listed here:
/// the game resolves the arrival tile from the same table at load, so a listed pair is a real warp event.
/// </summary>
public sealed class WarpRegistry
{
    public const string SchemaId = "hgss-save.warp-spawns/v1";
    public const string FileName = "hgss-us.warp-spawns.json";

    private readonly Dictionary<(int Map, int Warp), WarpTarget> _byKey;

    private WarpRegistry(Dictionary<(int Map, int Warp), WarpTarget> byKey, string path)
    {
        _byKey = byKey;
        Path = path;
    }

    public string Path { get; }

    public int Count => _byKey.Count;

    public bool TryGet(int map, int warp, out WarpTarget target) => _byKey.TryGetValue((map, warp), out target!);

    public IEnumerable<WarpTarget> ForMap(int map) =>
        _byKey.Values.Where(w => w.MapId == map).OrderBy(w => w.WarpIndex);

    public static WarpRegistry LoadDefault(string? explicitPath = null)
    {
        var path = explicitPath ?? Environment.GetEnvironmentVariable("HGSS_WARP_SPAWNS") ?? FindDefault();
        if (path is null || !File.Exists(path))
            throw HgssException.BadArguments("warps-missing",
                $"Warp table not found ({path ?? "(null)"}). Pass --warp-spawns PATH or set HGSS_WARP_SPAWNS; location.warp is refused without it.");
        return Load(path);
    }

    private static string? FindDefault()
    {
        var candidates = new List<string>();
        var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null)
            candidates.Add(System.IO.Path.Combine(exeDir, FileName));
        var cwd = Directory.GetCurrentDirectory();
        candidates.Add(System.IO.Path.Combine(cwd, FileName));
        candidates.Add(System.IO.Path.Combine(cwd, "registry", FileName));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static WarpRegistry Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw HgssException.BadArguments("warps-io", $"Cannot read warp table '{path}': {e.Message}");
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != SchemaId)
                throw HgssException.BadArguments("warps-schema", $"Warp table '{path}' must declare schema '{SchemaId}'.");
            if (!root.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Array)
                throw HgssException.BadArguments("warps-shape", $"Warp table '{path}' must define a 'maps' array.");
            var byKey = new Dictionary<(int, int), WarpTarget>();
            foreach (var map in maps.EnumerateArray())
            {
                var mapId = map.GetProperty("map_id").GetInt32();
                var symbol = map.TryGetProperty("map_symbol", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                if (!map.TryGetProperty("warp_arrivals", out var arrivals) || arrivals.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var arrival in arrivals.EnumerateArray())
                {
                    var index = arrival.GetProperty("warp_index").GetInt32();
                    var destination = arrival.TryGetProperty("destination_symbol", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    byKey[(mapId, index)] = new WarpTarget(mapId, symbol, index,
                        arrival.GetProperty("x").GetInt32(), arrival.GetProperty("y").GetInt32(), destination);
                }
            }

            return new WarpRegistry(byKey, path);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw HgssException.BadArguments("warps-parse", $"Warp table '{path}' is not valid: {e.Message}");
        }
    }
}
