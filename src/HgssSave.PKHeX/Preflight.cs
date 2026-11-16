using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace HgssSave.PKHeX;

/// <summary>
/// Read-only preflight scan of one raw image: per-copy integrity plus which copy
/// PKHeX would activate. Uses PKHeX's own <c>Checksums.CRC16_CCITT</c> and
/// <c>SAV4BlockDetection.CompareFooters</c>; only the footer geometry is copied
/// (see <see cref="HgssLayout"/>).
/// </summary>
internal static class Preflight
{
    public static Core.PreflightReport Scan(byte[] bytes)
    {
        var copies = new List<Core.BlockCopyInfo>(4);
        copies.Add(ScanMain(bytes, "general", HgssLayout.GeneralSize, 0));
        copies.Add(ScanMain(bytes, "general", HgssLayout.GeneralSize, HgssLayout.PartitionSize));
        copies.Add(ScanMain(bytes, "storage", HgssLayout.StorageSize, HgssLayout.StorageStart));
        copies.Add(ScanMain(bytes, "storage", HgssLayout.StorageSize, HgssLayout.StorageStart + HgssLayout.PartitionSize));

        var warnings = new List<string>();
        var activeGeneral = ActivePartition(bytes, HgssLayout.GeneralSize, 0, "general", copies, warnings);
        var activeStorage = ActivePartition(bytes, HgssLayout.StorageSize, HgssLayout.StorageStart, "storage", copies, warnings);
        return new Core.PreflightReport(copies, activeGeneral, activeStorage, warnings);
    }

    private static Core.BlockCopyInfo ScanMain(byte[] bytes, string block, int length, int offset)
    {
        var span = bytes.AsSpan(offset, length);
        var major = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[(length - HgssLayout.CounterFromEnd)..]);
        var minor = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[(length - HgssLayout.FooterSize)..]);
        var recordedSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[(length - 0x0C)..]);
        var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[(length - 0x08)..]);
        var saved = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span[(length - 2)..]);
        var computed = Checksums.CRC16_CCITT(span[..(length - HgssLayout.FooterSize)]);
        var initialized = major != 0xFFFFFFFF || minor != 0xFFFFFFFF;
        var partition = offset < HgssLayout.PartitionSize ? Core.Partition.A : Core.Partition.B;
        return new Core.BlockCopyInfo(block, partition, offset, length, initialized, saved == computed,
            saved, computed, major, minor, magic, (uint)recordedSize);
    }

    private static Core.Partition ActivePartition(
        byte[] bytes, int length, int start, string block,
        List<Core.BlockCopyInfo> copies, List<string> warnings)
    {
        var pick = SAV4BlockDetection.CompareFooters(bytes, start + length - HgssLayout.CounterFromEnd,
            start + HgssLayout.PartitionSize + length - HgssLayout.CounterFromEnd);
        var active = pick == 0 ? Core.Partition.A : Core.Partition.B;
        var activeCopy = copies.Find(c => c.Block == block && c.Partition == active);
        var otherCopy = copies.Find(c => c.Block == block && c.Partition != active);
        if (activeCopy is not null && otherCopy is not null
            && activeCopy.CounterOrKey == otherCopy.CounterOrKey
            && activeCopy.CounterSecondary == otherCopy.CounterSecondary
            && activeCopy.Initialized && otherCopy.Initialized)
        {
            warnings.Add($"{block} block revision counters are tied; PKHeX prefers partition A.");
        }

        return active;
    }

    /// <summary>True when the copies PKHeX activates are present and CRC-clean.</summary>
    public static bool ActiveCopiesValid(Core.PreflightReport report)
    {
        foreach (var copy in report.Copies)
        {
            var isActive = (copy.Block == "general" && copy.Partition == report.ActiveGeneral)
                || (copy.Block == "storage" && copy.Partition == report.ActiveStorage);
            if (isActive && (!copy.Initialized || !copy.CrcValid))
                return false;
        }

        return true;
    }
}
