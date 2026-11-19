using System;
using System.Buffers.Binary;
using HgssSave.PKHeX;
using PKHeX.Core;
using Xunit;

namespace HgssSave.Tests;

/// <summary>
/// Partition selection follows the game's decision tree
/// (<c>save.c:429-526</c>): one partition per boot, and the general and
/// storage counters inside that partition must agree. These cases are the ones
/// the corpus cannot produce, so they are built here.
/// </summary>
public sealed class SelectionTests
{
    private const int GeneralSize = 0xF628;
    private const int StorageStart = 0xF700;
    private const int StorageSize = 0x12310;
    private const int Partition = 0x40000;

    /// <summary>Two valid partitions, A older than B, agreeing counters inside each.</summary>
    private static byte[] TwoPartitions(uint older = 9, uint newer = 10)
    {
        // A synthesized blank carries no shaped footer (PKHeX writes only the
        // CRC), so the footers are written here in full.
        var bytes = new byte[0x80000];
        WriteFooter(bytes, 0, GeneralSize, 0, older);
        WriteFooter(bytes, StorageStart, StorageSize, 1, older);
        WriteFooter(bytes, Partition, GeneralSize, 0, newer);
        WriteFooter(bytes, StorageStart + Partition, StorageSize, 1, newer);
        return bytes;
    }

    private static void WriteFooter(byte[] bytes, int block, int size, int slot, uint count)
    {
        var footer = bytes.AsSpan(block + size - 0x10, 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(footer, count);
        BinaryPrimitives.WriteUInt32LittleEndian(footer[4..], (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(footer[8..], 0x20060623);
        BinaryPrimitives.WriteUInt16LittleEndian(footer[12..], (ushort)slot);
        var crc = Checksums.CRC16_CCITT(bytes.AsSpan(block, size - 0x10));
        BinaryPrimitives.WriteUInt16LittleEndian(footer[14..], crc);
    }

    private static void BreakPayload(byte[] bytes, int block)
    {
        bytes[block + 0x100] ^= 0xFF;
    }

    [Fact]
    public void NewestPartitionWithAgreeingCountersLoads()
    {
        var report = Preflight.Scan(TwoPartitions());
        Assert.Equal(Preflight.IsGood, report.LoadStatus);
        Assert.Equal(Core.Partition.B, report.ActiveGeneral);
        Assert.Equal(Core.Partition.B, report.ActiveStorage);
        Assert.True(Preflight.ActiveCopiesValid(report));
    }

    [Fact]
    public void BrokenGeneralCrcInNewestFallsBackToOlder()
    {
        var bytes = TwoPartitions();
        BreakPayload(bytes, Partition);
        var report = Preflight.Scan(bytes);
        Assert.Equal(Preflight.SlotFail, report.LoadStatus);
        Assert.Equal(Core.Partition.A, report.ActiveGeneral);
        Assert.Equal(Core.Partition.A, report.ActiveStorage);
        Assert.Contains("corrupted", string.Join(" ", report.Warnings));
        Assert.False(Preflight.ActiveCopiesValid(report));
    }

    [Fact]
    public void BrokenStorageCrcInNewestFallsBackToOlder()
    {
        var bytes = TwoPartitions();
        BreakPayload(bytes, StorageStart + Partition);
        var report = Preflight.Scan(bytes);
        Assert.Equal(Preflight.SlotFail, report.LoadStatus);
        Assert.Equal(Core.Partition.A, report.ActivePartition());
    }

    [Fact]
    public void CountersDisagreeingInsideNewestFallBack()
    {
        var bytes = TwoPartitions();
        // The counter sits outside the CRC, so this edit leaves the chunk valid.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Partition + GeneralSize - 0x10), 11);
        var report = Preflight.Scan(bytes);
        Assert.Equal(Preflight.SlotFail, report.LoadStatus);
        Assert.Equal(Core.Partition.A, report.ActivePartition());
    }

    [Fact]
    public void BothGeneralCopiesBrokenErasesTheSave()
    {
        var bytes = TwoPartitions();
        BreakPayload(bytes, 0);
        BreakPayload(bytes, Partition);
        var report = Preflight.Scan(bytes);
        Assert.Equal(Preflight.TotalFail, report.LoadStatus);
        Assert.False(Preflight.ActiveCopiesValid(report));
    }

    [Fact]
    public void SplitSelectionIsNotLoadable()
    {
        // General current in A, storage current in B: PKHeX's per-block rule
        // would call this a valid pair. The game cannot load it.
        var bytes = TwoPartitions();
        BreakPayload(bytes, Partition);            // p1 general invalid
        BreakPayload(bytes, StorageStart);         // p0 storage invalid
        var report = Preflight.Scan(bytes);
        Assert.Equal(Preflight.TotalFail, report.LoadStatus);
        Assert.False(Preflight.ActiveCopiesValid(report));
    }
}

internal static class PreflightReportExtensions
{
    public static Core.Partition ActivePartition(this Core.PreflightReport report)
    {
        Assert.Equal(report.ActiveGeneral, report.ActiveStorage);
        return report.ActiveGeneral;
    }
}
