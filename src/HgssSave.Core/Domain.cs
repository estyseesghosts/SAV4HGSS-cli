using System.Collections.Generic;

namespace HgssSave.Core;

/// <summary>Which physical partition a block copy lives in.</summary>
public enum Partition { A, B }

/// <summary>
/// Domain-level pocket names. Mirrors PKHeX's <c>InventoryType</c> values used by
/// <c>PlayerBag4HGSS.GetPouches</c>, without exposing PKHeX types to callers (design §3,
/// "The central design rule").
/// </summary>
public enum PouchKind
{
    Items,
    KeyItems,
    Machines,
    Mail,
    Medicine,
    Berries,
    Balls,
    BattleItems,
}

/// <summary>Classification of an indexed symbol, from the pinned decompilation headers.</summary>
public enum SymbolKind
{
    PersistentFlag,
    TemporaryFlag,
    SaveWork,
    TemporaryVar,
    SpecialRuntimeVar,
    Map,
    Item,
    Species,
    Move,
    RangeConstant,
    Alias,
    Unknown,
}

public static class SymbolKinds
{
    /// <summary>JSON spelling of <see cref="SymbolKind"/>, per design §7 <c>storage_kind</c>.</summary>
    public static string ToJson(SymbolKind kind) => kind switch
    {
        SymbolKind.PersistentFlag => "persistent_flag",
        SymbolKind.TemporaryFlag => "temporary_flag",
        SymbolKind.SaveWork => "save_work",
        SymbolKind.TemporaryVar => "temporary_var",
        SymbolKind.SpecialRuntimeVar => "special_runtime_var",
        SymbolKind.Map => "map",
        SymbolKind.Item => "item",
        SymbolKind.Species => "species",
        SymbolKind.Move => "move",
        SymbolKind.RangeConstant => "range_constant",
        SymbolKind.Alias => "alias",
        _ => "unknown",
    };

    public static SymbolKind FromJson(string value) => value switch
    {
        "persistent_flag" => SymbolKind.PersistentFlag,
        "temporary_flag" => SymbolKind.TemporaryFlag,
        "save_work" => SymbolKind.SaveWork,
        "temporary_var" => SymbolKind.TemporaryVar,
        "special_runtime_var" => SymbolKind.SpecialRuntimeVar,
        "map" => SymbolKind.Map,
        "item" => SymbolKind.Item,
        "species" => SymbolKind.Species,
        "move" => SymbolKind.Move,
        "range_constant" => SymbolKind.RangeConstant,
        "alias" => SymbolKind.Alias,
        _ => SymbolKind.Unknown,
    };

    /// <summary>True when the symbol names a field that a patch may write into a raw save.</summary>
    public static bool IsWritableField(SymbolKind kind) =>
        kind is SymbolKind.PersistentFlag or SymbolKind.SaveWork;
}

/// <summary>Identity of a parsed save: what the bytes actually say.</summary>
public sealed record SaveIdentity(
    string Family,
    string Game,
    int GameCode,
    int Language,
    long Bytes);

/// <summary>Trainer profile fields read from the active general block.</summary>
public sealed record TrainerSnapshot(
    string Name,
    int Gender,
    uint Money,
    uint MaxMoney,
    int Coins,
    int JohtoBadges,
    int KantoBadges,
    ushort Tid16,
    ushort Sid16,
    uint Id32,
    int PlayedHours,
    int PlayedMinutes,
    int PlayedSeconds);

/// <summary>
/// Saved overworld position. <see cref="MapId"/>/<see cref="X"/>/<see cref="Y"/> are the
/// primary fields; <see cref="SecondaryX"/>/<see cref="SecondaryY"/>/<see cref="Z"/> are the
/// mirrored fields PKHeX maintains (SAV4HGSS.cs <c>X2</c>/<c>Y2</c>/<c>Z</c>).
/// </summary>
public sealed record LocationSnapshot(
    int MapId,
    int X,
    int Y,
    int SecondaryX,
    int SecondaryY,
    int Z,
    string? MapSymbol);

public sealed record PartySlotInfo(
    int Index,
    bool Present,
    bool IsEgg,
    int Species,
    string SpeciesName,
    string Nickname,
    int Form,
    int Level,
    int HeldItem,
    string? HeldItemName,
    int Gender,
    ushort Tid16,
    ushort Sid16,
    int Experience,
    int Happiness,
    IReadOnlyList<int> Moves);

public sealed record BagEntry(
    PouchKind Pouch,
    int Slot,
    int ItemId,
    string? ItemName,
    int Quantity);

public sealed record PouchCapacity(PouchKind Pouch, int Slots, int Occupied, int MaxQuantityPerItem);

public sealed record BoxSlotInfo(
    int Box,
    int Slot,
    bool Present,
    bool IsEgg,
    int Species,
    string SpeciesName,
    string Nickname,
    int Level);

public sealed record DexSummary(int Seen, int Caught, int Total);

/// <summary>Integrity of one physical block copy, computed independently of PKHeX's block selection.</summary>
public sealed record BlockCopyInfo(
    string Block,
    Partition Partition,
    int Offset,
    int Length,
    bool Initialized,
    bool CrcValid,
    ushort CrcSaved,
    ushort CrcComputed,
    uint CounterOrKey,
    uint CounterSecondary,
    uint Magic,
    uint RecordedSize);

/// <summary>Which copy PKHeX selected for each independently-versioned block.</summary>
public sealed record PreflightReport(
    IReadOnlyList<BlockCopyInfo> Copies,
    Partition ActiveGeneral,
    Partition ActiveStorage,
    IReadOnlyList<string> Warnings);

/// <summary>Structural validation outcome for a save (design §9 "Level A").</summary>
public sealed record StructuralValidation(
    bool Passed,
    bool SizeOk,
    bool IdentityOk,
    bool ChecksumsValid,
    bool PreflightOk,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> ChecksumInfo);

/// <summary>Everything the CLI reads out of a save, in domain terms.</summary>
public sealed record SaveSnapshot(
    SaveIdentity Identity,
    PreflightReport Preflight,
    TrainerSnapshot Trainer,
    LocationSnapshot Location,
    IReadOnlyList<PartySlotInfo> Party,
    IReadOnlyList<BagEntry> Bag,
    IReadOnlyList<PouchCapacity> Pouches,
    IReadOnlyList<BoxSlotInfo> Boxes,
    DexSummary Dex,
    int FlagCount,
    int WorkCount,
    int WorkBaseId,
    int FlagsSet,
    int WorkNonZero);
