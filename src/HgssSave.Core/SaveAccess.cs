using System;
using System.Collections.Generic;

namespace HgssSave.Core;

/// <summary>Result of serializing a session: the bytes plus re-parse checksum status.</summary>
public readonly record struct ExportOutcome(
    byte[] Bytes,
    bool ChecksumsValid,
    IReadOnlyList<string> ChecksumInfo);

/// <summary>
/// An editable save session. All reads and writes are expressed in domain terms;
/// implementations are the only place that may touch save offsets (design §3).
/// </summary>
public interface ISaveSession : IDisposable
{
    SaveIdentity Identity { get; }

    PreflightReport Preflight { get; }

    /// <summary>Bytes as loaded, never mutated. Used for base hashing and byte auditing.</summary>
    byte[] OriginalBytes { get; }

    /// <summary>Bytes of the in-memory save after applied mutations (before export).</summary>
    byte[] CurrentBytes { get; }

    TrainerSnapshot Trainer { get; }

    LocationSnapshot Location { get; }

    SaveSnapshot Snapshot();

    StructuralValidation ValidateStructural();

    /// <summary>Number of persistent flag bits the format stores.</summary>
    int FlagCount { get; }

    /// <summary>Number of save-work slots the format stores.</summary>
    int WorkCount { get; }

    /// <summary>Decompilation variable ID that maps to work slot 0 (0x4000 for HGSS).</summary>
    int WorkBaseId { get; }

    bool TryGetFlag(int flagId, out bool value);

    /// <summary>Reads a save-work value by decompilation variable ID (0x4000 + slot).</summary>
    bool TryGetVar(int varId, out int value);

    void SetFlag(int flagId, bool value);

    void SetVar(int varId, int value);

    void SetMoney(uint money);

    /// <summary>Reads the quantity of an item in the bag; false when the item is not in any pocket.</summary>
    bool TryGetBagQuantity(int itemId, out int quantity, out PouchKind pouch);

    void SetBagQuantity(int itemId, int quantity);

    /// <summary>Bag entries as currently loaded, after any applied mutation.</summary>
    IReadOnlyList<BagEntry> BagEntries();

    /// <summary>Indices of persistent flags that are currently set.</summary>
    IReadOnlyList<int> SetFlagIds();

    /// <summary>All save-work slots as (variable id, value).</summary>
    IReadOnlyDictionary<int, int> WorkValues();

    /// <summary>True when the item id belongs to one of the HGSS pockets, regardless of current quantity.</summary>
    bool IsPocketableItem(int itemId);

    /// <summary>Validated preview of a .pk4 file: species, checksum status, level and display name.</summary>
    PkPreview PreviewPkFile(byte[] pkFileBytes);

    int PartySpecies(int slot);

    int BoxSpecies(int box, int slot);

    void ImportPartySlot(int slot, byte[] pkFileBytes, bool updateDex);

    void ClearPartySlot(int slot);

    void ImportBoxSlot(int box, int slot, byte[] pkFileBytes, bool updateDex);

    void ClearBoxSlot(int box, int slot);

    byte[] ExportPartySlot(int slot);

    byte[] ExportBoxSlot(int box, int slot);

    /// <summary>Serializes with checksum repair. Does not write to disk.</summary>
    ExportOutcome Export();
}

/// <summary>Blank-save flavour. See <c>BlankSave</c> in HgssSave.PKHeX for the evidence behind each.</summary>
public enum BlankKind
{
    /// <summary>Erased-flash container: no game-written save structure. Not patchable.</summary>
    ErasedFlash,

    /// <summary>Structure-valid container with checksums written. Structurally valid, not story-verified.</summary>
    FreshStructure,
}

public sealed record BlankOptions(
    string Game,
    string? TrainerName,
    int? Tid16,
    int? Sid16,
    int? Gender,
    int? Language);

/// <summary>Recognized on-disk container shapes. Only <see cref="Raw"/> is editable.</summary>
public enum ContainerKind
{
    Raw,
    DeSmuME,
    NoCash,
    Other,
}

/// <summary>Stable display strings for container kinds in reports.</summary>
public static class ContainerStrings
{
    public static string ToDisplay(ContainerKind kind) => kind switch
    {
        ContainerKind.Raw => "raw",
        ContainerKind.DeSmuME => "dsv",
        ContainerKind.NoCash => "nocash",
        _ => "other",
    };
}

/// <summary>A documented byte range in the raw save, supplied by the format adapter.</summary>
public sealed record ByteRegion(
    string Name,
    Partition Partition,
    int Start,
    int End,
    string Category,
    string Evidence);

/// <summary>Opens save sessions and produces container/raw conversions.</summary>
public interface ISaveFactory
{
    /// <summary>Library version string reported in outputs for provenance.</summary>
    string LibraryVersion { get; }

    /// <summary>Documented byte regions, used to classify binary diffs (design §9 "Useful diff model").</summary>
    IReadOnlyList<ByteRegion> KnownRegions { get; }

    /// <summary>Strict open: exact raw size, HG/SS identity, valid checksums, unambiguous block copies.</summary>
    ISaveSession Open(string path);

    /// <summary>Strict open of in-memory bytes (used to re-parse exports).</summary>
    ISaveSession OpenBytes(byte[] bytes, string origin);

    /// <summary>Identifies the container shape of a file without mutating or silently converting it.</summary>
    ContainerKind ProbeContainer(string path);

    /// <summary>Extracts a raw HGSS payload from a recognized container, validating it first.</summary>
    byte[] UnwrapContainer(string path);

    byte[] SynthesizeBlank(BlankKind kind, BlankOptions options);
}
