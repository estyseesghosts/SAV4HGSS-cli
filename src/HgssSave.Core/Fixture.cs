using System.Collections.Generic;
using System.Text.Json;

namespace HgssSave.Core;

/// <summary>Curated story fixture recipe (design §8). A human-readable intent plus a
/// transactional patch against a known-valid base save. Base saves stay private;
/// only hashes and provenance are committed.</summary>
public sealed record FixtureRecipe(
    string Schema,
    string Id,
    string Game,
    string BasePath,
    string? BaseSha256,
    PatchDocument Patch,
    string PositionMode,
    IReadOnlyList<string> RuntimeCheckpoints,
    string ReviewStatus,
    string ReviewNotes)
{
    public const string SchemaId = "hgss-save.fixture/v1";
}

/// <summary>Deterministic build manifest (design §9).</summary>
public sealed record FixtureManifest(
    string Schema,
    string FixtureId,
    string InputSha256,
    string PatchSha256,
    string OutputSha256,
    string PkhexCoreVersion,
    string RegistryRevision,
    string Game,
    string StructuralValidation,
    string SemanticValidation,
    string? RuntimeResult)
{
    public const string SchemaId = "hgss-save.manifest/v1";
}

public static class FixtureParser
{
    public static FixtureRecipe Parse(byte[] json, string origin)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw HgssException.BadArguments("fixture-shape", $"Fixture recipe root must be an object ({origin}).");

        string Get(string name)
        {
            if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(v.GetString()))
                throw HgssException.BadArguments("fixture-missing-key", $"Fixture recipe is missing '{name}' ({origin}).");
            return v.GetString()!;
        }

        var schema = Get("schema");
        if (schema != FixtureRecipe.SchemaId)
            throw HgssException.BadArguments("fixture-schema", $"Fixture schema '{schema}' is not supported (expected {FixtureRecipe.SchemaId}).");

        var id = Get("id");
        var game = Get("game").ToUpperInvariant();
        if (game is not ("HG" or "SS"))
            throw HgssException.BadArguments("fixture-game", $"Fixture game must be 'HG' or 'SS' (got '{game}').");

        if (!root.TryGetProperty("base", out var baseEl) || baseEl.ValueKind != JsonValueKind.Object)
            throw HgssException.BadArguments("fixture-base", $"Fixture recipe must define a 'base' object with 'path' ({origin}).");
        if (!baseEl.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(pathEl.GetString()))
            throw HgssException.BadArguments("fixture-base", $"Fixture 'base.path' must be a non-empty string ({origin}).");
        var basePath = pathEl.GetString()!;
        string? baseSha = null;
        if (baseEl.TryGetProperty("sha256", out var shaEl))
        {
            if (shaEl.ValueKind != JsonValueKind.String || shaEl.GetString()!.Length != 64)
                throw HgssException.BadArguments("fixture-base-sha", $"Fixture 'base.sha256' must be 64 hex characters ({origin}).");
            baseSha = shaEl.GetString();
        }

        if (!root.TryGetProperty("patch", out var patchEl) || patchEl.ValueKind != JsonValueKind.Object)
            throw HgssException.BadArguments("fixture-patch", $"Fixture recipe must define a 'patch' object with 'operations' ({origin}).");
        string patchText = patchEl.GetRawText();
        using (var patchDoc = JsonDocument.Parse(patchText))
        {
            if (!patchDoc.RootElement.TryGetProperty("schema", out _))
            {
                var injected = "{\"schema\":\"" + PatchDocument.SchemaId + "\",\"game\":\"" + game + "\"," + patchText.TrimStart('{');
                patchText = injected;
            }
        }

        var patchDocParsed = PatchParser.Parse(System.Text.Encoding.UTF8.GetBytes(patchText), origin + "#patch");

        var positionMode = root.TryGetProperty("position", out var posEl) && posEl.ValueKind == JsonValueKind.Object
            && posEl.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String
            ? modeEl.GetString() ?? "preserve-base"
            : "preserve-base";
        if (positionMode != "preserve-base")
            throw HgssException.Unsafe("fixture-position", $"Only position.mode 'preserve-base' is supported; map teleports need a reviewed safe-spawn record ({origin}).");

        var checkpoints = new List<string>();
        if (root.TryGetProperty("runtime", out var rtEl) && rtEl.ValueKind == JsonValueKind.Object
            && rtEl.TryGetProperty("checkpoints", out var cpEl) && cpEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in cpEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                    checkpoints.Add(item.GetString()!);
            }
        }

        var reviewStatus = "requires-runtime-verification";
        var reviewNotes = "";
        if (root.TryGetProperty("review", out var revEl) && revEl.ValueKind == JsonValueKind.Object)
        {
            if (revEl.TryGetProperty("status", out var sEl) && sEl.ValueKind == JsonValueKind.String)
                reviewStatus = sEl.GetString() ?? reviewStatus;
            if (revEl.TryGetProperty("notes", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                reviewNotes = nEl.GetString() ?? "";
        }

        return new FixtureRecipe(schema, id, game, basePath, baseSha, patchDocParsed, positionMode, checkpoints, reviewStatus, reviewNotes);
    }
}
