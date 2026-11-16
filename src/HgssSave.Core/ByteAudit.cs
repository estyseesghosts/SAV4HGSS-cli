using System;
using System.Collections.Generic;
using System.Linq;

namespace HgssSave.Core;

/// <summary>Binary-diff annotations from design §9 "Useful diff model".</summary>
public static class ByteCategories
{
    public const string Requested = "requested-data";
    public const string Bookkeeping = "PKHeX-export-bookkeeping";
    public const string Checksum = "checksum";
    public const string Unclassified = "unclassified";
}

public sealed record ByteRange(
    Partition Partition,
    int Start,
    int End,
    int Length,
    string BeforeHex,
    string AfterHex,
    string Category,
    string Region);

public sealed record ByteAuditResult(
    int ChangedBytes,
    int RequestedBytes,
    int BookkeepingBytes,
    int ChecksumBytes,
    int UnclassifiedBytes,
    IReadOnlyList<ByteRange> Ranges,
    bool Accepted,
    string? RejectionReason);

/// <summary>
/// Byte-level auditing. The patch audit compares exported bytes against the original bytes
/// and requires every change to be either requested by an operation or part of the measured
/// export bookkeeping baseline (design §6 step 6: "Unknown modifications are errors in strict mode").
/// </summary>
public static class ByteAudit
{
    public static IReadOnlyList<int> ChangedIndices(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        var length = Math.Max(before.Length, after.Length);
        var changed = new List<int>();
        for (var i = 0; i < length; i++)
        {
            var a = i < before.Length ? before[i] : (byte)0;
            var b = i < after.Length ? after[i] : (byte)0;
            if (a != b)
                changed.Add(i);
        }

        return changed;
    }

    /// <summary>Groups changed indices into contiguous ranges.</summary>
    public static IReadOnlyList<(int Start, int End)> RangesFromIndices(IReadOnlyList<int> indices)
    {
        var ranges = new List<(int, int)>();
        if (indices.Count == 0)
            return ranges;

        var sorted = indices.OrderBy(i => i).ToList();
        var start = sorted[0];
        var end = sorted[0];
        foreach (var index in sorted.Skip(1))
        {
            if (index == end + 1)
            {
                end = index;
                continue;
            }

            ranges.Add((start, end));
            start = index;
            end = index;
        }

        ranges.Add((start, end));
        return ranges;
    }

    public static ByteRegion? RegionFor(IReadOnlyList<ByteRegion> regions, Partition partition, int index)
    {
        foreach (var region in regions)
        {
            if (region.Partition == partition && index >= region.Start && index <= region.End)
                return region;
        }

        return null;
    }

    public static Partition PartitionFor(int index) => index < 0x40000 ? Partition.A : Partition.B;

    private static string CategoryFromRegion(ByteRegion? region) => region?.Category switch
    {
        ByteRegionCategory.Requested => ByteCategories.Requested,
        ByteRegionCategory.Checksum => ByteCategories.Checksum,
        ByteRegionCategory.Bookkeeping => ByteCategories.Bookkeeping,
        _ => ByteCategories.Unclassified,
    };

    /// <summary>
    /// Classifies a diff between two arbitrary save images. Nothing is "accepted" here: the
    /// caller decides whether unclassified changes are tolerable for their comparison.
    /// </summary>
    public static ByteAuditResult ClassifyDiff(
        byte[] before,
        byte[] after,
        IReadOnlyList<ByteRegion> regions,
        int maxRanges)
    {
        var changed = ChangedIndices(before, after);
        return Build(changed, before, after, regions, requested: null, bookkeeping: null, checksums: null, accepted: null, maxRanges);
    }

    /// <summary>
    /// Patch audit: <paramref name="requested"/> are bytes written by the operations,
    /// <paramref name="bookkeeping"/> are bytes a no-edit export changes by itself.
    /// Checksum-footer bytes are always allowed: a recomputed CRC over edited data
    /// legitimately differs from both the original and the baseline value.
    /// Any other changed byte fails the audit.
    /// </summary>
    public static ByteAuditResult AuditPatch(
        byte[] original,
        byte[] exported,
        IReadOnlyList<int> requested,
        IReadOnlyList<int> bookkeeping,
        IReadOnlyList<ByteRegion> regions,
        int maxRanges)
    {
        var changed = ChangedIndices(original, exported);
        var requestedSet = new HashSet<int>(requested);
        var bookkeepingSet = new HashSet<int>(bookkeeping);
        var checksumSet = new HashSet<int>();
        for (var i = 0; i < original.Length && i < exported.Length; i++)
        {
            if (RegionFor(regions, PartitionFor(i), i)?.Category == ByteRegionCategory.Checksum)
                checksumSet.Add(i);
        }

        var unexplained = changed.Where(i => !requestedSet.Contains(i) && !bookkeepingSet.Contains(i) && !checksumSet.Contains(i)).ToList();

        var result = Build(changed, original, exported, regions, requestedSet, bookkeepingSet, checksumSet, unexplained.Count == 0, maxRanges);
        return unexplained.Count == 0
            ? result
            : result with
            {
                Accepted = false,
                RejectionReason = $"{unexplained.Count} changed byte(s) are outside the requested and bookkeeping allowlists; first offsets: {string.Join(", ", unexplained.Take(8).Select(i => $"0x{i:X5}"))}",
            };
    }

    private static ByteAuditResult Build(
        IReadOnlyList<int> changed,
        byte[] before,
        byte[] after,
        IReadOnlyList<ByteRegion> regions,
        IReadOnlySet<int>? requested,
        IReadOnlySet<int>? bookkeeping,
        IReadOnlySet<int>? checksums,
        bool? accepted,
        int maxRanges)
    {
        var requestedCount = requested is null ? 0 : changed.Count(i => requested.Contains(i));
        var bookkeepingCount = bookkeeping is null ? 0 : changed.Count(i => !requested!.Contains(i) && bookkeeping.Contains(i) && (checksums is null || !checksums.Contains(i)));
        var ranges = new List<ByteRange>();
        var checksumCount = 0;
        var unclassifiedCount = 0;

        foreach (var (start, end) in RangesFromIndices(changed))
        {
            var partition = PartitionFor(start);
            var categoryCounts = new Dictionary<string, int>();
            for (var i = start; i <= end; i++)
            {
                var category = CategoryForIndex(i, partition, requested, bookkeeping, checksums, regions);
                categoryCounts[category] = categoryCounts.GetValueOrDefault(category) + 1;
                if (category == ByteCategories.Checksum)
                    checksumCount++;
                else if (category == ByteCategories.Unclassified)
                    unclassifiedCount++;
            }

            var dominant = categoryCounts.OrderByDescending(kv => kv.Value).First().Key;
            var region = RegionFor(regions, partition, start);
            var hexBefore = Convert.ToHexString(before.AsSpan(start, end - start + 1).ToArray());
            var hexAfter = Convert.ToHexString(after.AsSpan(start, end - start + 1).ToArray());
            ranges.Add(new ByteRange(
                partition,
                start,
                end,
                end - start + 1,
                Truncate(hexBefore, 48),
                Truncate(hexAfter, 48),
                dominant,
                region?.Name ?? "unassigned"));
        }

        return new ByteAuditResult(
            changed.Count,
            requestedCount,
            bookkeepingCount,
            checksumCount,
            unclassifiedCount,
            ranges.Take(maxRanges).ToList(),
            accepted ?? true,
            null);
    }

    private static string CategoryForIndex(
        int index,
        Partition partition,
        IReadOnlySet<int>? requested,
        IReadOnlySet<int>? bookkeeping,
        IReadOnlySet<int>? checksums,
        IReadOnlyList<ByteRegion> regions)
    {
        if (requested is not null && requested.Contains(index))
            return ByteCategories.Requested;

        if (bookkeeping is not null && bookkeeping.Contains(index))
        {
            var region = RegionFor(regions, partition, index);
            return region?.Category == ByteRegionCategory.Checksum ? ByteCategories.Checksum : ByteCategories.Bookkeeping;
        }

        if (checksums is not null && checksums.Contains(index))
            return ByteCategories.Checksum;

        if (requested is null)
            return CategoryFromRegion(RegionFor(regions, partition, index));

        return ByteCategories.Unclassified;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

/// <summary>Category labels used by adapter-supplied byte regions.</summary>
public static class ByteRegionCategory
{
    public const string Requested = "requested-data";
    public const string Checksum = "checksum";
    public const string Bookkeeping = "bookkeeping";
    public const string Unassigned = "unassigned";
}
