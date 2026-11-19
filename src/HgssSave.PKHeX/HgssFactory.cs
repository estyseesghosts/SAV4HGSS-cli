using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using PKHeX.Core;
using HgssSave.Core;

namespace HgssSave.PKHeX;

/// <summary>
/// Opens strict HGSS sessions and converts recognized containers.
/// Only <see cref="HgssSession"/> touches <c>SAV4HGSS</c> (design §3).
/// </summary>
public sealed class HgssFactory : ISaveFactory
{
    public string LibraryVersion { get; } = ReadLibraryVersion();

    public IReadOnlyList<ByteRegion> KnownRegions { get; } = BuildRegions();

    public ISaveSession Open(string path) => OpenWithRegistry(path, null);

    public ISaveSession OpenBytes(byte[] bytes, string origin) => OpenBytesWithRegistry(bytes, origin, null);

    public ISaveSession OpenWithRegistry(string path, SymbolRegistry? registry)
    {
        byte[] file;
        try
        {
            file = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw HgssException.OutputConflict("input-io", $"Cannot read '{path}': {e.Message}");
        }

        if (file.Length != HgssLayout.RawSize)
        {
            throw HgssException.UnsupportedSave("input-size",
                $"Expected a raw {HgssLayout.RawSize}-byte HGSS save, found {file.Length} bytes. " +
                (LooksLikeDsv(file) ? "This looks like a DeSmuME .dsv file; run 'unwrap' first. Never strip containers implicitly." : "Only raw 0x80000 saves are supported."));
        }

        return OpenBytesWithRegistry(file, path, registry);
    }

    public ISaveSession OpenBytesWithRegistry(byte[] bytes, string origin, SymbolRegistry? registry)
    {
        if (bytes.Length != HgssLayout.RawSize)
            throw HgssException.UnsupportedSave("input-size", $"Expected {HgssLayout.RawSize} bytes, found {bytes.Length} ({origin}).");

        var working = (byte[])bytes.Clone();
        var sav = new SAV4HGSS(working);
        if (sav.Version is not (GameVersion.HG or GameVersion.SS))
        {
            throw HgssException.UnsupportedSave("save-identity",
                $"Not an HGSS save (game version {(int)sav.Version}). Only HeartGold and SoulSilver are supported.");
        }

        // The game's own selection runs first: PKHeX picks each block by counter
        // without validating it, so on a damaged image it reports "small block
        // checksum is invalid" where the useful answer is what the game would do.
        var preflight = Preflight.Scan(working);
        if (!Preflight.ActiveCopiesValid(preflight))
        {
            var detail = preflight.Warnings.Count > 0 ? " " + string.Join(" ", preflight.Warnings) : string.Empty;
            throw HgssException.Structural("save-preflight",
                $"The save is not in the state the game would load (preflight status {preflight.LoadStatus}). "
                + $"Refusing to edit a damaged save.{detail}");
        }

        if (!sav.ChecksumsValid)
        {
            throw HgssException.Structural("save-checksum",
                $"Save failed checksum verification: {sav.ChecksumInfo}");
        }

        return new HgssSession(sav, (byte[])bytes.Clone(), origin, preflight, registry);
    }

    public ContainerKind ProbeContainer(string path)
    {
        byte[] file;
        try
        {
            file = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw HgssException.OutputConflict("input-io", $"Cannot read '{path}': {e.Message}");
        }

        if (file.Length == HgssLayout.RawSize)
            return ContainerKind.Raw;
        if (LooksLikeDsv(file))
            return ContainerKind.DeSmuME;
        return ContainerKind.Other;
    }

    public byte[] UnwrapContainer(string path)
    {
        var kind = ProbeContainer(path);
        switch (kind)
        {
            case ContainerKind.Raw:
                return File.ReadAllBytes(path);
            case ContainerKind.DeSmuME:
            {
                var file = File.ReadAllBytes(path);
                return file[..HgssLayout.RawSize];
            }

            default:
                throw HgssException.UnsupportedSave("container",
                    $"Unrecognized container for '{path}'. Only raw 0x80000 saves and DeSmuME .dsv files are supported.");
        }
    }

    public byte[] SynthesizeBlank(BlankKind kind, BlankOptions options)
    {
        if (kind == BlankKind.ErasedFlash)
        {
            var erased = new byte[HgssLayout.RawSize];
            Array.Fill(erased, (byte)0xFF);
            return erased;
        }

        var version = options.Game.ToUpperInvariant() switch
        {
            "HG" => GameVersion.HG,
            "SS" => GameVersion.SS,
            _ => throw HgssException.BadArguments("blank-game", $"Blank game must be 'HG' or 'SS' (got '{options.Game}')."),
        };

        // NOTE: a default-constructed SAV4HGSS has no backing buffer and cannot be
        // exported (BlockInfo4 footer math runs off the end). Build on a zeroed raw
        // image instead; the result is structurally valid but story-unverified.
        var sav = new SAV4HGSS(new byte[HgssLayout.RawSize]);
        sav.Version = version;
        if (options.TrainerName is not null)
            sav.OT = options.TrainerName;
        if (options.Tid16 is not null)
            sav.TID16 = (ushort)CheckedU16(options.Tid16.Value, "--tid");
        if (options.Sid16 is not null)
            sav.SID16 = (ushort)CheckedU16(options.Sid16.Value, "--sid");
        if (options.Gender is not null)
        {
            if (options.Gender.Value is not (0 or 1))
                throw HgssException.BadArguments("blank-gender", "Gender must be 0 or 1.");
            sav.Gender = (byte)options.Gender.Value;
        }

        if (options.Language is not null)
        {
            if (options.Language.Value is < 1 or > 8)
                throw HgssException.BadArguments("blank-language", "Language must be 1..8.");
            sav.Language = options.Language.Value;
        }
        else
        {
            sav.Language = 2;
        }

        var blank = sav.Write().ToArray();
        WritePartitionFooters(blank);
        return blank;
    }

    /// <summary>
    /// Fresh-structure blanks come back from PKHeX with only the CRC written.
    /// The game rejects a chunk whose size, magic or slot disagree
    /// (<c>save.c:338-347</c>), so a blank needs the full footer to be shaped
    /// like a save the game would load.
    /// </summary>
    private static void WritePartitionFooters(byte[] bytes)
    {
        for (var partition = 0; partition < 2; partition++)
        {
            var baseOff = partition * HgssLayout.PartitionSize;
            WriteFooter(bytes, baseOff, HgssLayout.GeneralSize, 0);
            WriteFooter(bytes, baseOff + HgssLayout.StorageStart, HgssLayout.StorageSize, 1);
        }
    }

    private static void WriteFooter(byte[] bytes, int offset, int size, int slot)
    {
        var footer = bytes.AsSpan(offset + size - HgssLayout.FooterSize, HgssLayout.FooterSize);
        BinaryPrimitives.WriteUInt32LittleEndian(footer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(footer[4..], (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(footer[8..], 0x20060623);
        BinaryPrimitives.WriteUInt16LittleEndian(footer[12..], (ushort)slot);
        BinaryPrimitives.WriteUInt16LittleEndian(footer[14..],
            Checksums.CRC16_CCITT(bytes.AsSpan(offset, size - HgssLayout.FooterSize)));
    }

    private static int CheckedU16(int value, string name)
    {
        if (value is < 0 or > ushort.MaxValue)
            throw HgssException.BadArguments("blank-id", $"{name} must be 0..65535.");
        return value;
    }

    private static bool LooksLikeDsv(byte[] file) =>
        file.Length == HgssLayout.RawSize + HgssLayout.DsvFooterSize
        && file.AsSpan(HgssLayout.RawSize).EndsWith(Encoding.ASCII.GetBytes(HgssLayout.DsvMagic));

    private static string ReadLibraryVersion()
    {
        var assembly = typeof(SAV4HGSS).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            return info.Split('+')[0];
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static IReadOnlyList<ByteRegion> BuildRegions()
    {
        var regions = new List<ByteRegion>();
        foreach (var partition in new[] { Partition.A, Partition.B })
        {
            var baseOff = partition == Partition.B ? HgssLayout.PartitionSize : 0;
            regions.Add(new ByteRegion("general.data", partition, baseOff, baseOff + HgssLayout.GeneralSize - HgssLayout.FooterSize,
                ByteRegionCategory.Requested, "SAV4HGSS.cs:34 general block minus FooterSize (:38)"));
            regions.Add(new ByteRegion("general.footer", partition, baseOff + HgssLayout.GeneralSize - HgssLayout.FooterSize, baseOff + HgssLayout.GeneralSize,
                ByteRegionCategory.Checksum, "SAV4.cs:133-143 SetChecksums; Checksums.CRC16_CCITT"));
            regions.Add(new ByteRegion("general.gap", partition, baseOff + HgssLayout.GeneralSize, baseOff + HgssLayout.StorageStart,
                ByteRegionCategory.Unassigned, "docs/save-format.md §1 gap 0xF628-0xF6FF"));
            regions.Add(new ByteRegion("storage.data", partition, baseOff + HgssLayout.StorageStart, baseOff + HgssLayout.StorageStart + HgssLayout.StorageSize - HgssLayout.FooterSize,
                ByteRegionCategory.Requested, "SAV4HGSS.cs:35 storage block minus FooterSize (:38)"));
            regions.Add(new ByteRegion("storage.box-changed-bits", partition, baseOff + HgssLayout.StorageStart + HgssLayout.BoxChangedOffset, baseOff + HgssLayout.StorageStart + HgssLayout.BoxChangedOffset + 4,
                ByteRegionCategory.Bookkeeping, "SAV4HGSS.cs:52-58 GetFinalData forces 0x3FFFF"));
            regions.Add(new ByteRegion("storage.footer", partition, baseOff + HgssLayout.StorageStart + HgssLayout.StorageSize - HgssLayout.FooterSize, baseOff + HgssLayout.StorageStart + HgssLayout.StorageSize,
                ByteRegionCategory.Checksum, "SAV4.cs:133-143 SetChecksums"));
            foreach (var (offset, length, name) in HgssLayout.ExtraBlocks)
            {
                regions.Add(new ByteRegion($"extra.{name}", partition, baseOff + offset, baseOff + offset + length - 0x10,
                    ByteRegionCategory.Bookkeeping, "SAV4HGSS.cs:40-48 ExtraBlocks; BlockInfo4.cs:12 footer 0x10"));
                regions.Add(new ByteRegion($"extra.{name}.footer", partition, baseOff + offset + length - 0x10, baseOff + offset + length,
                    ByteRegionCategory.Checksum, "BlockInfo4.cs:42-51 checksum over block minus 2 bytes"));
            }
        }

        return regions;
    }
}
