using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace HgssSave.PKHeX;

/// <summary>
/// Read-only preflight scan of one raw image: per-copy integrity plus the save
/// status the game itself would reach at boot.
/// <para>
/// Selection is not PKHeX's per-block rule. The game validates both copies of
/// the general block and both copies of the storage block, then picks one
/// partition and requires the two counters <em>inside it</em> to agree
/// (<c>save.c:429-526</c>). This mirrors that decision tree, so a split file -
/// general current in one partition, storage in the other - is reported as
/// unloadable instead of being read as a valid pair.
/// </para>
/// </summary>
internal static class Preflight
{
    public const string IsGood = "IS_GOOD";
    public const string SlotFail = "SLOT_FAIL";
    public const string TotalFail = "TOTAL_FAIL";
    public const string NotExist = "NOT_EXIST";

    public static Core.PreflightReport Scan(byte[] bytes)
    {
        var copies = new List<Core.BlockCopyInfo>(4);
        copies.Add(ScanMain(bytes, "general", HgssLayout.GeneralSize, 0, 0));
        copies.Add(ScanMain(bytes, "general", HgssLayout.GeneralSize, HgssLayout.PartitionSize, 0));
        copies.Add(ScanMain(bytes, "storage", HgssLayout.StorageSize, HgssLayout.StorageStart, 1));
        copies.Add(ScanMain(bytes, "storage", HgssLayout.StorageSize, HgssLayout.StorageStart + HgssLayout.PartitionSize, 1));

        var warnings = new List<string>();
        var general = Copies(copies, "general");
        var storage = Copies(copies, "storage");
        var (partition, status, note) = Select(general, storage);
        if (note.Length != 0)
            warnings.Add(note);

        // With no loadable partition there is nothing to name; keep A so the
        // report still renders, and let the status do the talking.
        var active = partition ?? Core.Partition.A;
        return new Core.PreflightReport(copies, active, active, warnings, status);
    }

    private static Core.BlockCopyInfo[] Copies(List<Core.BlockCopyInfo> copies, string block)
    {
        var found = new Core.BlockCopyInfo[2];
        foreach (var copy in copies)
        {
            if (copy.Block != block)
                continue;
            var index = copy.Partition == Core.Partition.A ? 0 : 1;
            found[index] = copy;
        }

        return found;
    }

    private static Core.BlockCopyInfo ScanMain(byte[] bytes, string block, int length, int offset, int slot)
    {
        var span = bytes.AsSpan(offset, length);
        var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[(length - HgssLayout.CounterFromEnd)..]);
        var payloadDword = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[(length - HgssLayout.PayloadDwordFromEnd)..]);
        var recordedSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[(length - 0x0C)..]);
        var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[(length - 0x08)..]);
        var chunkSlot = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span[(length - HgssLayout.SlotFromEnd)..]);
        var saved = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span[(length - 2)..]);
        var computed = Checksums.CRC16_CCITT(span[..(length - HgssLayout.FooterSize)]);
        var shaped = recordedSize == (uint)length
            && magic is 0x20060623 or 0x20070903
            && chunkSlot == slot;
        var partition = offset < HgssLayout.PartitionSize ? Core.Partition.A : Core.Partition.B;
        return new Core.BlockCopyInfo(block, partition, offset, length, shaped, saved == computed,
            saved, computed, count, payloadDword, magic, (uint)recordedSize);
    }

    /// <summary>True only when the game would load the newest state without a warning.</summary>
    public static bool ActiveCopiesValid(Core.PreflightReport report)
    {
        if (report.LoadStatus != IsGood)
            return false;

        foreach (var copy in report.Copies)
        {
            var isActive = (copy.Block == "general" || copy.Block == "storage")
                && copy.Partition == report.ActiveGeneral;
            if (isActive && (!copy.Initialized || !copy.CrcValid))
                return false;
        }

        return true;
    }

    private static uint Counter(Core.BlockCopyInfo copy) =>
        copy.Initialized && copy.CrcValid ? copy.CounterOrKey : 0u;

    /// <summary><c>SaveSlotCheckCompare</c> (save.c:391-421): valid count, newer, older.</summary>
    private static (int Good, int Newer, int Older) Compare(Core.BlockCopyInfo a, Core.BlockCopyInfo b)
    {
        var va = a.Initialized && a.CrcValid;
        var vb = b.Initialized && b.CrcValid;
        if (va && vb)
            return Counter(a) >= Counter(b) ? (2, 0, 1) : (2, 1, 0);
        if (va)
            return (1, 0, 2);
        if (vb)
            return (1, 1, 2);
        return (0, 2, 2);
    }

    /// <summary><c>Save_GetSaveFilesStatus</c> (save.c:429-526).</summary>
    private static (Core.Partition? Partition, string Status, string Note) Select(
        Core.BlockCopyInfo[] general, Core.BlockCopyInfo[] storage)
    {
        var (goodMain, newerMain, olderMain) = Compare(general[0], general[1]);
        var (goodSub, newerSub, olderSub) = Compare(storage[0], storage[1]);

        static Core.Partition P(int index) => index == 0 ? Core.Partition.A : Core.Partition.B;

        static string Fallback(int index) =>
            $"The newest partition is inconsistent, so the game loads partition {P(index)} and announces "
            + "\"The save file is corrupted. The previous save file will be loaded.\"";

        const string Erased =
            "The game finds no consistent partition and erases the save "
            + "(\"The save file will be erased due to corruption or damage.\").";

        if (goodMain == 0 && goodSub == 0)
            return (null, NotExist, "No valid general or storage copy; the game treats this as a new game.");
        if (goodMain == 0 || goodSub == 0)
            return (null, TotalFail, Erased);

        if (goodMain == 2 && goodSub == 2)
        {
            if (Counter(general[newerMain]) == Counter(storage[newerMain]))
                return (P(newerMain), IsGood, string.Empty);
            if (Counter(general[olderMain]) != Counter(storage[olderMain]))
                return (null, TotalFail, Erased);
            return (P(olderMain), SlotFail, Fallback(olderMain));
        }

        if (goodMain == 1 && goodSub == 2)
        {
            if (Counter(general[newerMain]) == Counter(storage[newerMain]))
                return (P(newerMain), SlotFail, Fallback(newerMain));
            return (null, TotalFail, Erased);
        }

        if (goodMain == 2 && goodSub == 1)
        {
            if (Counter(general[newerMain]) == Counter(storage[newerMain]))
                return (P(newerMain), IsGood, string.Empty);
            if (olderMain != 2 && Counter(general[olderMain]) == Counter(storage[olderMain]))
                return (P(olderMain), SlotFail, Fallback(olderMain));
            return (null, TotalFail, Erased);
        }

        if (goodMain == 1 && goodSub == 1 && newerMain == newerSub)
            return (P(newerMain), IsGood, string.Empty);

        _ = olderSub;
        return (null, TotalFail, Erased);
    }
}
