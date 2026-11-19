// Layout constants copied from PKHeX.Core 26.10.18 private/protected members.
// Each value cites its source line; HgssLayoutConsistencyTests verifies them
// against PKHeX's own behavior so a future package update fails loudly.
namespace HgssSave.PKHeX;

/// <summary>Raw HGSS save geometry and pocket sizes.</summary>
internal static class HgssLayout
{
    /// <summary>Full raw save, two 0x40000 partitions (docs/save-format.md §1; SaveUtil.SIZE_G4RAW).</summary>
    public const int RawSize = 0x80000;

    /// <summary>Bytes per partition (SAV4.cs:20 <c>PartitionSize</c>).</summary>
    public const int PartitionSize = 0x40000;

    /// <summary>Public in PKHeX (SAV4HGSS.cs:34 <c>GeneralSize</c>); kept here for symmetry.</summary>
    public const int GeneralSize = 0xF628;

    /// <summary>PC storage starts at GeneralSize + gap (SAV4HGSS.cs:20,35: <c>GeneralSize + GeneralGap</c>).</summary>
    public const int StorageStart = 0xF700;

    /// <summary>PC storage block length (SAV4HGSS.cs:35 <c>StorageSize</c>).</summary>
    public const int StorageSize = 0x12310;

    /// <summary>Gap between the general block and PC storage (SAV4HGSS.cs:36 <c>GeneralGap</c>).</summary>
    public const int GeneralGap = 0xD8;

    /// <summary>Main-block footer excluded from the CRC (SAV4HGSS.cs:38 <c>FooterSize</c>).</summary>
    public const int FooterSize = 0x10;

    /// <summary>
    /// Save counter, the first footer field (<c>struct SaveChunkFooter</c> in
    /// <c>include/save.h</c>; written at <c>save.c:370</c>). The game compares
    /// this, never the dword before it.
    /// </summary>
    public const int CounterFromEnd = 0x10;

    /// <summary>
    /// Last payload dword, inside the CRC range. PKHeX reads it as a "major
    /// counter" (<c>SAV4.cs:180-184</c>) but it is not one: it is 0 in the
    /// general and storage blocks of the corpus saves. Kept only for reports.
    /// </summary>
    public const int PayloadDwordFromEnd = 0x14;

    /// <summary>Chunk slot field, <c>u16</c>, 0 = general and 1 = storage (<c>save.c:344-346</c>).</summary>
    public const int SlotFromEnd = 0x04;

    /// <summary>Script-variable region start, general-relative (SAV4HGSS.cs:61 <c>EventWork</c>).</summary>
    public const int EventWork = 0xDE4;

    /// <summary>Persistent flag region start, general-relative (SAV4HGSS.cs:62 <c>EventFlag</c>).</summary>
    public const int EventFlag = 0x10C4;

    /// <summary>Decompilation variable base: slot index = varId - 0x4000 (vars.h <c>VAR_BASE</c>; design §6).</summary>
    public const int WorkBase = 0x4000;

    /// <summary>Bag start, general-relative (PlayerBag4HGSS.cs:9 <c>BaseOffset</c>).</summary>
    public const int BagBase = 0x644;

    /// <summary>Changed-box bitmask, storage-relative (SAV4HGSS.cs:136-140).</summary>
    public const int BoxChangedOffset = 0x12004;

    /// <summary>Auxiliary blocks: (partition-relative offset, length) (SAV4HGSS.cs:40-48).</summary>
    public static readonly (int Offset, int Length, string Name)[] ExtraBlocks =
    [
        (0x23000, 0x2AC0, "hall-of-fame"),
        (0x26000, 0x0BB0, "battle-hall"),
        (0x27000, 0x1D60, "battle-video-0"),
        (0x29000, 0x1D60, "battle-video-1"),
        (0x2B000, 0x1D60, "battle-video-2"),
        (0x2D000, 0x1D60, "battle-video-3"),
    ];

    /// <summary>
    /// Physical game pocket capacities in slots (pokeheartgold items.h:34-41
    /// <c>NUM_BAG_ITEMS/MEDICINE/BALLS/TMS_HMS/BERRIES/MAIL/BATTLE_ITEMS/KEY_ITEMS</c>).
    /// These differ from PKHeX's pouch array lengths (e.g. GeneralPt has 192 ids for 165
    /// slots, Battle has 13 ids for 32 slots), so the adapter clamps to these.
    /// </summary>
    public static int PhysicalSlots(Core.PouchKind pouch) => pouch switch
    {
        Core.PouchKind.Items => 165,
        Core.PouchKind.KeyItems => 50,
        Core.PouchKind.Machines => 101,
        Core.PouchKind.Mail => 12,
        Core.PouchKind.Medicine => 40,
        Core.PouchKind.Berries => 64,
        Core.PouchKind.Balls => 24,
        Core.PouchKind.BattleItems => 32,
        _ => 0,
    };

    /// <summary>DeSmuME .dsv footer: raw save plus this trailer (SaveHandlerDeSmuME.cs:10-14).</summary>
    public const int DsvFooterSize = 0x7A;

    public const string DsvMagic = "|-DESMUME SAVE-|";
}
