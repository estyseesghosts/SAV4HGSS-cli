using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HgssSave.Core;

/// <summary>Supported mutation operations (design §5). Each is idempotent set semantics.</summary>
public abstract record PatchOperation
{
    public abstract string OpName { get; }

    /// <summary>Reference to a symbolic or numeric id, when the operation addresses one.</summary>
    public virtual string? SymbolReference => null;
}

public sealed record FlagSetOperation(string Reference, bool Value, bool? Expect) : PatchOperation
{
    public override string OpName => "flag.set";
    public override string? SymbolReference => Reference;
}

public sealed record VariableSetOperation(string Reference, int Value, int? Expect) : PatchOperation
{
    public override string OpName => "variable.set";
    public override string? SymbolReference => Reference;
}

/// <summary>Johto/Kanto badge bitmasks (PlayerProfile.johtoBadges/kantoBadges, player_data.c:114-150); null leaves that byte untouched.</summary>
public sealed record BadgesSetOperation(int? Johto, int? Kanto) : PatchOperation
{
    /// <summary>Also apply what the game does when each newly gained badge is won (BadgeRules); default false in recipes.</summary>
    public bool Consequences { get; init; }

    public override string OpName => "trainer.badges.set";
}

public sealed record MoneySetOperation(uint Value, uint? Expect) : PatchOperation
{
    public override string OpName => "trainer.money.set";
}

public sealed record BagQuantitySetOperation(string Reference, int Quantity, int? Expect) : PatchOperation
{
    public override string OpName => "inventory.quantity.set";
    public override string? SymbolReference => Reference;
}

public sealed record PartyImportOperation(int Slot, string File, int? ExpectSpecies, bool UpdateDex) : PatchOperation
{
    public override string OpName => "party.slot.import";
}

public sealed record PartyClearOperation(int Slot) : PatchOperation
{
    public override string OpName => "party.slot.clear";
}

public sealed record BoxImportOperation(int Box, int Slot, string File, int? ExpectSpecies, bool UpdateDex) : PatchOperation
{
    public override string OpName => "box.slot.import";
}

public sealed record BoxClearOperation(int Box, int Slot) : PatchOperation
{
    public override string OpName => "box.slot.clear";
}

public sealed record LocationSetOperation(int Map, int X, int Y, int Z) : PatchOperation
{
    public override string OpName => "location.set";
}

/// <summary>
/// Teleport via the game's own Continue path: sets flag 0x966 and the saved dynamic warp to
/// {Map, Warp} so Continue spawns on that warp event's tile with freshly initialised actors
/// (docs/state-log.md F5; field_warp_tasks.c FieldTask_ContinueGame_Normal).
/// </summary>
public sealed record LocationWarpOperation(int Map, int Warp, int Direction) : PatchOperation
{
    /// <summary>Warp tile from the warp table, filled in by the planner (not part of the recipe).</summary>
    public int? X { get; init; }

    public int? Y { get; init; }

    public override string OpName => "location.warp";
}

public sealed record DexSetOperation(int Species, bool Seen, bool Caught) : PatchOperation
{
    public override string OpName => "dex.set";
}

public abstract record PatchAssertion
{
    public abstract string Field { get; }
}

public sealed record FlagAssertion(string Reference, bool Value) : PatchAssertion
{
    public override string Field => $"flag.{Reference}";
}

public sealed record VariableAssertion(string Reference, int Value) : PatchAssertion
{
    public override string Field => $"variable.{Reference}";
}

public sealed record BagAssertion(string Reference, int Quantity) : PatchAssertion
{
    public override string Field => $"bag.{Reference}";
}

public sealed record MoneyAssertion(uint Value) : PatchAssertion
{
    public override string Field => "trainer.money";
}

public sealed record CoinsAssertion(int Value) : PatchAssertion
{
    public override string Field => "trainer.coins";
}

public sealed record JohtoBadgesAssertion(int Value) : PatchAssertion
{
    public override string Field => "trainer.badges_johto";
}

public sealed record KantoBadgesAssertion(int Value) : PatchAssertion
{
    public override string Field => "trainer.badges_kanto";
}

public sealed record PartyCountAssertion(int Value) : PatchAssertion
{
    public override string Field => "party.count";
}

public sealed record PartySpeciesAssertion(int Slot, int Value) : PatchAssertion
{
    public override string Field => $"party.{Slot}.species";
}

public sealed record DexSeenAssertion(int Species, bool Value) : PatchAssertion
{
    public override string Field => $"dex.{Species}.seen";
}

public sealed record DexCaughtAssertion(int Species, bool Value) : PatchAssertion
{
    public override string Field => $"dex.{Species}.caught";
}

public sealed record LocationAssertion(int Map, int X, int Y, int Z) : PatchAssertion
{
    public override string Field => "location.spawn";
}

public sealed record BoxSpeciesAssertion(int Box, int Slot, int Value) : PatchAssertion
{
    public override string Field => $"box.{Box}.{Slot}.species";
}

public sealed record MapAssertion(int Value) : PatchAssertion
{
    public override string Field => "location.map_id";
}

public sealed record GameAssertion(string Value) : PatchAssertion
{
    public override string Field => "game";
}

public sealed record ChecksumsAssertion(bool Value) : PatchAssertion
{
    public override string Field => "checksums.valid";
}

public sealed record PatchDocument(
    string Schema,
    string? Game,
    string? BaseSha256,
    string? SymbolRegistryRevision,
    IReadOnlyList<PatchOperation> Operations,
    IReadOnlyList<PatchAssertion> Assertions)
{
    public const string SchemaId = "hgss-save.patch/v1";
}

/// <summary>
/// Strict reader for patch.v1 documents. Unknown keys, duplicate keys, wrong types and
/// unknown operations are rejected rather than ignored (design §5: "No shell or source-code
/// execution in patch expressions. This is a data format").
/// </summary>
public static class PatchParser
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    public static PatchDocument Parse(byte[] json, string origin)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Options);
        }
        catch (JsonException e)
        {
            throw HgssException.BadArguments("patch-json", $"Patch recipe is not valid JSON ({origin}): {e.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw HgssException.BadArguments("patch-shape", $"Patch recipe root must be an object ({origin}).");

            RejectDuplicates(root, origin);

            var schema = RequireString(root, "schema", origin);
            if (schema != PatchDocument.SchemaId)
                throw HgssException.BadArguments("patch-schema", $"Patch recipe schema '{schema}' is not supported (expected {PatchDocument.SchemaId}).");

            var game = OptionalString(root, "game", origin);
            if (game is not null)
                game = NormalizeGame(game);

            var baseSha = OptionalString(root, "base_sha256", origin);
            if (baseSha is not null && !IsValidSha256(baseSha))
                throw HgssException.BadArguments("patch-base-sha", "base_sha256 must be 64 hexadecimal characters.");

            var registry = OptionalString(root, "symbol_registry", origin);

            var operations = new List<PatchOperation>();
            if (root.TryGetProperty("operations", out var ops) && ops.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ops.EnumerateArray())
                    operations.Add(ParseOperation(item, origin));
            }
            else
            {
                throw HgssException.BadArguments("patch-operations", $"Patch recipe must define an 'operations' array ({origin}).");
            }

            var assertions = new List<PatchAssertion>();
            if (root.TryGetProperty("assert", out var assertEl) && assertEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in assertEl.EnumerateArray())
                    assertions.Add(ParseAssertion(item, origin));
            }

            foreach (var key in root.EnumerateObject())
            {
                if (key.Name is not ("schema" or "game" or "base_sha256" or "symbol_registry" or "operations" or "assert"))
                    throw HgssException.BadArguments("patch-unknown-key", $"Unknown patch recipe key '{key.Name}' ({origin}).");
            }

            return new PatchDocument(schema, game, baseSha, registry, operations, assertions);
        }
    }

    private static void RejectDuplicates(JsonElement element, string origin)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
        {
            if (!seen.Add(prop.Name))
                throw HgssException.BadArguments("patch-duplicate-key", $"Duplicate key '{prop.Name}' ({origin}).");
        }
    }

    private static PatchOperation ParseOperation(JsonElement item, string origin)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw HgssException.BadArguments("patch-operation-shape", $"Each operation must be an object ({origin}).");

        RejectDuplicates(item, origin);
        var op = RequireString(item, "op", origin);
        var allowed = op switch
        {
            "flag.set" => new[] { "op", "id", "value", "expect" },
            "variable.set" => new[] { "op", "id", "value", "expect" },
            "trainer.money.set" => new[] { "op", "value", "expect" },
        "trainer.badges.set" => new[] { "op", "johto", "kanto", "consequences" },
            "inventory.quantity.set" => new[] { "op", "item", "quantity", "expect" },
            "party.slot.import" => new[] { "op", "slot", "file", "expect_species", "update_dex" },
            "party.slot.clear" => new[] { "op", "slot" },
            "box.slot.import" => new[] { "op", "box", "slot", "file", "expect_species", "update_dex" },
            "box.slot.clear" => new[] { "op", "box", "slot" },
            "location.set" => new[] { "op", "map", "x", "y", "z" },
        "location.warp" => new[] { "op", "map", "warp", "direction" },
            "dex.set" => new[] { "op", "species", "seen", "caught" },
            _ => throw HgssException.BadArguments("patch-unknown-op", $"Unknown operation '{op}' ({origin}). Supported: flag.set, variable.set, trainer.money.set, trainer.badges.set, inventory.quantity.set, party.slot.import, party.slot.clear, box.slot.import, box.slot.clear, location.set, location.warp, dex.set."),
        };

        foreach (var key in item.EnumerateObject())
        {
            if (!allowed.Contains(key.Name))
                throw HgssException.BadArguments("patch-unknown-key", $"Unknown key '{key.Name}' for operation '{op}' ({origin}).");
        }

        switch (op)
        {
            case "flag.set":
            {
                var id = RequireReference(item, "id", op, origin);
                var value = RequireBool(item, "value", op, origin);
                var expect = OptionalBool(item, "expect", op, origin);
                return new FlagSetOperation(id, value, expect);
            }
            case "variable.set":
            {
                var id = RequireReference(item, "id", op, origin);
                var value = RequireIntInRange(item, "value", op, origin, ushort.MinValue, ushort.MaxValue);
                var expect = item.TryGetProperty("expect", out _)
                    ? RequireIntInRange(item, "expect", op, origin, ushort.MinValue, ushort.MaxValue)
                    : (int?)null;
                return new VariableSetOperation(id, value, expect);
            }
            case "trainer.money.set":
            {
                var value = RequireUInt(item, "value", op, origin);
                var expect = item.TryGetProperty("expect", out _) ? RequireUInt(item, "expect", op, origin) : (uint?)null;
                return new MoneySetOperation(value, expect);
            }
            case "inventory.quantity.set":
            {
                var itemRef = RequireReference(item, "item", op, origin);
                var quantity = RequireIntInRange(item, "quantity", op, origin, 0, 999);
                var expect = item.TryGetProperty("expect", out _) ? RequireIntInRange(item, "expect", op, origin, 0, 999) : (int?)null;
                return new BagQuantitySetOperation(itemRef, quantity, expect);
            }
            case "party.slot.import":
            {
                var slot = RequireIntInRange(item, "slot", op, origin, 0, 5);
                var file = RequireString(item, "file", origin);
                var expectSpecies = item.TryGetProperty("expect_species", out _)
                    ? RequireIntInRange(item, "expect_species", op, origin, 1, 493)
                    : (int?)null;
                var updateDex = item.TryGetProperty("update_dex", out _)
                    ? RequireBool(item, "update_dex", op, origin)
                    : true;
                return new PartyImportOperation(slot, file, expectSpecies, updateDex);
            }
            case "party.slot.clear":
            {
                var slot = RequireIntInRange(item, "slot", op, origin, 0, 5);
                return new PartyClearOperation(slot);
            }
            case "box.slot.import":
            {
                var box = RequireIntInRange(item, "box", op, origin, 0, 17);
                var slot = RequireIntInRange(item, "slot", op, origin, 0, 29);
                var file = RequireString(item, "file", origin);
                var expectSpecies = item.TryGetProperty("expect_species", out _)
                    ? RequireIntInRange(item, "expect_species", op, origin, 1, 493)
                    : (int?)null;
                var updateDex = item.TryGetProperty("update_dex", out _)
                    ? RequireBool(item, "update_dex", op, origin)
                    : true;
                return new BoxImportOperation(box, slot, file, expectSpecies, updateDex);
            }
            case "box.slot.clear":
            {
                var box = RequireIntInRange(item, "box", op, origin, 0, 17);
                var slot = RequireIntInRange(item, "slot", op, origin, 0, 29);
                return new BoxClearOperation(box, slot);
            }
            case "location.set":
            {
                var map = RequireIntInRange(item, "map", op, origin, 0, 0xFFFF);
                var x = RequireIntInRange(item, "x", op, origin, 0, 0xFFFF);
                var y = RequireIntInRange(item, "y", op, origin, 0, 0xFFFF);
                var z = RequireIntInRange(item, "z", op, origin, 0, 0xFFFF);
                return new LocationSetOperation(map, x, y, z);
            }
            case "trainer.badges.set":
            {
                int? johto = item.TryGetProperty("johto", out _) ? RequireIntInRange(item, "johto", op, origin, 0, 255) : null;
                int? kanto = item.TryGetProperty("kanto", out _) ? RequireIntInRange(item, "kanto", op, origin, 0, 255) : null;
                if (johto is null && kanto is null)
                    throw HgssException.BadArguments("patch-badges", $"trainer.badges.set needs johto and/or kanto ({origin}).");
                var consequences = item.TryGetProperty("consequences", out _) && RequireBool(item, "consequences", op, origin);
                return new BadgesSetOperation(johto, kanto) { Consequences = consequences };
            }
            case "location.warp":
            {
                var map = RequireIntInRange(item, "map", op, origin, 0, 0xFFFF);
                var warp = RequireIntInRange(item, "warp", op, origin, 0, 0xFF);
                var direction = item.TryGetProperty("direction", out _) ? RequireIntInRange(item, "direction", op, origin, 0, 3) : 1;
                return new LocationWarpOperation(map, warp, direction);
            }
            case "dex.set":
            default:
            {
                var species = RequireIntInRange(item, "species", op, origin, 1, 493);
                var seen = RequireBool(item, "seen", op, origin);
                var caught = RequireBool(item, "caught", op, origin);
                if (caught && !seen)
                    throw HgssException.BadArguments("dex-consistency", $"dex.set for species {species}: caught implies seen; set seen true.");
                return new DexSetOperation(species, seen, caught);
            }
        }
    }

    private static PatchAssertion ParseAssertion(JsonElement item, string origin)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw HgssException.BadArguments("patch-assert-shape", $"Each assertion must be an object ({origin}).");

        RejectDuplicates(item, origin);
        var field = RequireString(item, "field", origin);
        if (!item.TryGetProperty("equals", out var value))
            throw HgssException.BadArguments("patch-assert-value", $"Assertion for '{field}' must provide 'equals' ({origin}).");

        foreach (var key in item.EnumerateObject())
        {
            if (key.Name is not ("field" or "equals"))
                throw HgssException.BadArguments("patch-unknown-key", $"Unknown key '{key.Name}' in assertion ({origin}).");
        }

        switch (field)
        {
            case "trainer.money":
                return new MoneyAssertion(RequireUIntValue(value, field, origin));
            case "trainer.coins":
                return new CoinsAssertion(RequireIntValue(value, field, origin, 0, 50_000));
            case "trainer.badges_johto":
                return new JohtoBadgesAssertion(RequireIntValue(value, field, origin, 0, 0xFF));
            case "trainer.badges_kanto":
                return new KantoBadgesAssertion(RequireIntValue(value, field, origin, 0, 0xFF));
            case "party.count":
                return new PartyCountAssertion(RequireIntValue(value, field, origin, 0, 6));
            case "location.map_id":
                return new MapAssertion(RequireIntValue(value, field, origin, 0, 0xFFFF));
            case "game":
                return new GameAssertion(NormalizeGame(RequireStringValue(value, field, origin)));
            case "checksums.valid":
                return new ChecksumsAssertion(RequireBoolValue(value, field, origin));
            default:
            {
                var (prefix, reference) = SplitPrefixedField(field, origin);
                if (prefix == "party")
                {
                    var parts = reference.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0], out var slot) && slot >= 0 && slot <= 5 && parts[1] == "species")
                        return new PartySpeciesAssertion(slot, RequireIntValue(value, field, origin, 0, 493));
                    throw HgssException.BadArguments("patch-assert-field", $"Unknown assertion field '{field}' ({origin}). Expected 'party.<0-5>.species'.");
                }
                if (prefix == "dex")
                {
                    var parts = reference.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0], out var species) && species >= 1 && species <= 493)
                    {
                        if (parts[1] == "seen")
                            return new DexSeenAssertion(species, RequireBoolValue(value, field, origin));
                        if (parts[1] == "caught")
                            return new DexCaughtAssertion(species, RequireBoolValue(value, field, origin));
                    }

                    throw HgssException.BadArguments("patch-assert-field", $"Unknown assertion field '{field}' ({origin}). Expected 'dex.<1-493>.seen|caught'.");
                }
                if (prefix == "location" && reference == "spawn")
                {
                    if (value.ValueKind != JsonValueKind.Object)
                        throw HgssException.BadArguments("patch-assert-value", $"Assertion for '{field}' must provide an object with map/x/y/z ({origin}).");
                    var map = value.TryGetProperty("map", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : throw HgssException.BadArguments("patch-assert-value", $"Assertion for '{field}' needs numeric 'map' ({origin}).");
                    var x = value.TryGetProperty("x", out var xv) && xv.ValueKind == JsonValueKind.Number ? xv.GetInt32() : throw HgssException.BadArguments("patch-assert-value", $"Assertion for '{field}' needs numeric 'x' ({origin}).");
                    var y = value.TryGetProperty("y", out var yv) && yv.ValueKind == JsonValueKind.Number ? yv.GetInt32() : throw HgssException.BadArguments("patch-assert-value", $"Assertion for '{field}' needs numeric 'y' ({origin}).");
                    var z = value.TryGetProperty("z", out var zv) && zv.ValueKind == JsonValueKind.Number ? zv.GetInt32() : 0;
                    return new LocationAssertion(map, x, y, z);
                }
                if (prefix == "box")
                {
                    var parts = reference.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 && int.TryParse(parts[0], out var box) && box >= 0 && box <= 17
                        && int.TryParse(parts[1], out var slot) && slot >= 0 && slot <= 29 && parts[2] == "species")
                        return new BoxSpeciesAssertion(box, slot, RequireIntValue(value, field, origin, 0, 493));
                    throw HgssException.BadArguments("patch-assert-field", $"Unknown assertion field '{field}' ({origin}). Expected 'box.<0-17>.<0-29>.species'.");
                }
                return prefix switch
                {
                    "flag" => new FlagAssertion(reference, RequireBoolValue(value, field, origin)),
                    "variable" => new VariableAssertion(reference, RequireIntValue(value, field, origin, ushort.MinValue, ushort.MaxValue)),
                    "bag" => new BagAssertion(reference, RequireIntValue(value, field, origin, 0, 999)),
                    _ => throw HgssException.BadArguments("patch-assert-field", $"Unknown assertion field '{field}' ({origin})."),
                };
            }
        }
    }

    private static (string Prefix, string Reference) SplitPrefixedField(string field, string origin)
    {
        var index = field.IndexOf('.', StringComparison.Ordinal);
        if (index <= 0 || index == field.Length - 1)
            throw HgssException.BadArguments("patch-assert-field", $"Unknown assertion field '{field}' ({origin}).");

        return (field[..index], field[(index + 1)..]);
    }

    private static string NormalizeGame(string game)
    {
        var upper = game.Trim().ToUpperInvariant();
        if (upper is not ("HG" or "SS"))
            throw HgssException.BadArguments("patch-game", $"game must be 'HG' or 'SS' (got '{game}').");
        return upper;
    }

    private static bool IsValidSha256(string value)
    {
        if (value.Length != 64)
            return false;

        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }

    private static string RequireString(JsonElement item, string name, string origin)
    {
        if (!item.TryGetProperty(name, out var value))
            throw HgssException.BadArguments("patch-missing-key", $"Missing required key '{name}' ({origin}).");
        if (value.ValueKind != JsonValueKind.String)
            throw HgssException.BadArguments("patch-type", $"'{name}' must be a string ({origin}).");
        var text = value.GetString() ?? "";
        if (text.Length == 0)
            throw HgssException.BadArguments("patch-type", $"'{name}' must not be empty ({origin}).");
        return text;
    }

    private static string? OptionalString(JsonElement item, string name, string origin)
    {
        if (!item.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Null)
            throw HgssException.BadArguments("patch-type", $"'{name}' must not be null; omit the key to leave it unset ({origin}).");
        if (value.ValueKind != JsonValueKind.String)
            throw HgssException.BadArguments("patch-type", $"'{name}' must be a string ({origin}).");
        return value.GetString();
    }

    /// <summary>Accepts a symbol name or a numeric id (decimal or 0x-prefixed).</summary>
    private static string RequireReference(JsonElement item, string name, string op, string origin)
    {
        if (!item.TryGetProperty(name, out var value))
            throw HgssException.BadArguments("patch-missing-key", $"Missing required key '{name}' for '{op}' ({origin}).");

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
            {
                var text = value.GetString() ?? "";
                if (text.Length == 0)
                    throw HgssException.BadArguments("patch-type", $"'{name}' must not be empty ({origin}).");
                return text;
            }
            case JsonValueKind.Number:
            {
                if (!value.TryGetInt32(out var number))
                    throw HgssException.BadArguments("patch-type", $"'{name}' must be a 32-bit integer ({origin}).");
                return number.ToString(CultureInfo.InvariantCulture);
            }
            default:
                throw HgssException.BadArguments("patch-type", $"'{name}' must be a string or integer ({origin}).");
        }
    }

    private static bool RequireBool(JsonElement item, string name, string op, string origin)
    {
        if (!item.TryGetProperty(name, out var value))
            throw HgssException.BadArguments("patch-missing-key", $"Missing required key '{name}' for '{op}' ({origin}).");
        return RequireBoolValue(value, $"{op}.{name}", origin);
    }

    private static bool RequireBoolValue(JsonElement value, string context, string origin)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        throw HgssException.BadArguments("patch-type", $"'{context}' must be a boolean ({origin}).");
    }

    private static bool? OptionalBool(JsonElement item, string name, string op, string origin)
    {
        if (!item.TryGetProperty(name, out var value))
            return null;
        return RequireBoolValue(value, $"{op}.{name}", origin);
    }

    private static uint RequireUInt(JsonElement item, string name, string op, string origin)
    {
        if (!item.TryGetProperty(name, out var value))
            throw HgssException.BadArguments("patch-missing-key", $"Missing required key '{name}' for '{op}' ({origin}).");
        return RequireUIntValue(value, $"{op}.{name}", origin);
    }

    private static uint RequireUIntValue(JsonElement value, string context, string origin)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw HgssException.BadArguments("patch-type", $"'{context}' must be a number ({origin}).");
        if (!value.TryGetInt64(out var raw))
            throw HgssException.BadArguments("patch-type", $"'{context}' must be an integer ({origin}).");
        if (raw < uint.MinValue || raw > uint.MaxValue)
            throw HgssException.BadArguments("patch-type", $"'{context}' is outside the supported 32-bit range ({origin}).");
        return (uint)raw;
    }

    private static int RequireIntInRange(JsonElement item, string name, string op, string origin, int min, int max)
    {
        if (!item.TryGetProperty(name, out var value))
            throw HgssException.BadArguments("patch-missing-key", $"Missing required key '{name}' for '{op}' ({origin}).");
        return RequireIntValue(value, $"{op}.{name}", origin, min, max);
    }

    private static int RequireIntValue(JsonElement value, string context, string origin, int min, int max)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw HgssException.BadArguments("patch-type", $"'{context}' must be a number ({origin}).");
        if (!value.TryGetInt32(out var raw))
            throw HgssException.BadArguments("patch-type", $"'{context}' must be an integer ({origin}).");
        if (raw < min || raw > max)
            throw HgssException.BadArguments("patch-range", $"'{context}' must be between {min} and {max} (got {raw}) ({origin}).");
        return raw;
    }

    private static string RequireStringValue(JsonElement value, string context, string origin)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw HgssException.BadArguments("patch-type", $"'{context}' must be a string ({origin}).");
        var text = value.GetString() ?? "";
        if (text.Length == 0)
            throw HgssException.BadArguments("patch-type", $"'{context}' must not be empty ({origin}).");
        return text;
    }
}
