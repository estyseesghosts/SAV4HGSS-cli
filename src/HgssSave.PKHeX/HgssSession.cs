using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;
using HgssSave.Core;

namespace HgssSave.PKHeX;

/// <summary>
/// Editable session over one <c>SAV4HGSS</c>. All PKHeX contact lives here;
/// callers speak only in domain terms (design §3, "The central design rule").
/// </summary>
public sealed class HgssSession : ISaveSession
{
    private readonly SAV4HGSS _sav;
    private readonly SymbolRegistry? _registry;

    public HgssSession(SAV4HGSS sav, byte[] originalBytes, string origin, PreflightReport preflight, SymbolRegistry? registry)
    {
        _sav = sav;
        OriginalBytes = originalBytes;
        Origin = origin;
        Preflight = preflight;
        _registry = registry;
        Identity = Identify(sav, originalBytes.Length);
    }

    public string Origin { get; }

    public SaveIdentity Identity { get; }

    public PreflightReport Preflight { get; }

    public byte[] OriginalBytes { get; }

    public byte[] CurrentBytes => _sav.Data.ToArray();

    public int FlagCount => _sav.EventFlagCount;

    public int WorkCount => _sav.EventWorkCount;

    public int WorkBaseId => HgssLayout.WorkBase;

    public TrainerSnapshot Trainer => new(
        _sav.OT, _sav.Gender, _sav.Money, (uint)_sav.MaxMoney, (int)_sav.Coin,
        _sav.Badges, _sav.Badges16, _sav.TID16, _sav.SID16, _sav.ID32,
        _sav.PlayedHours, _sav.PlayedMinutes, _sav.PlayedSeconds);

    public LocationSnapshot Location => new(
        _sav.M, _sav.X, _sav.Y, _sav.X2, _sav.Y2, _sav.Z,
        _registry?.NameFor(SymbolKind.Map, _sav.M));

    public static SaveIdentity Identify(SAV4HGSS sav, int bytes) => sav.Version switch
    {
        GameVersion.HG => new SaveIdentity("SAV4HGSS", "HG", (int)sav.Version, sav.Language, bytes),
        GameVersion.SS => new SaveIdentity("SAV4HGSS", "SS", (int)sav.Version, sav.Language, bytes),
        _ => throw HgssException.UnsupportedSave("save-identity",
            $"Not an HGSS save (game version {(int)sav.Version}). Only HeartGold and SoulSilver are supported."),
    };

    public SaveSnapshot Snapshot()
    {
        var party = PartySlots();
        var bag = BagEntries();
        var pouches = PouchCapacities();
        var boxes = PresentBoxSlots();
        var (seen, caught) = DexCounts();
        var flagsSet = CountSetFlags();
        var workNonZero = CountNonZeroWork();
        return new SaveSnapshot(Identity, Preflight, Trainer, Location, party, bag, pouches,
            boxes, new DexSummary(seen, caught, _sav.MaxSpeciesID), FlagCount, WorkCount,
            WorkBaseId, flagsSet, workNonZero);
    }

    public StructuralValidation ValidateStructural()
    {
        var failures = new List<string>();
        var sizeOk = OriginalBytes.Length == HgssLayout.RawSize;
        if (!sizeOk)
            failures.Add($"expected {HgssLayout.RawSize} bytes, found {OriginalBytes.Length}.");

        bool identityOk;
        try
        {
            _ = Identify(_sav, OriginalBytes.Length);
            identityOk = true;
        }
        catch (HgssException)
        {
            identityOk = false;
            failures.Add($"game version {(int)_sav.Version} is not HG or SS.");
        }

        var fresh = new SAV4HGSS((byte[])OriginalBytes.Clone());
        var checksumsValid = sizeOk && fresh.ChecksumsValid;
        if (!checksumsValid)
            failures.Add("save checksums are invalid.");
        var info = fresh.ChecksumInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var preflightOk = HgssSave.PKHeX.Preflight.ActiveCopiesValid(Preflight);
        if (!preflightOk)
            failures.Add("the active general or storage copy failed its integrity check.");

        return new StructuralValidation(failures.Count == 0, sizeOk, identityOk, checksumsValid, preflightOk, failures, info);
    }

    public bool TryGetFlag(int flagId, out bool value)
    {
        if ((uint)flagId >= (uint)FlagCount)
        {
            value = false;
            return false;
        }

        value = _sav.GetEventFlag(flagId);
        return true;
    }

    public bool TryGetVar(int varId, out int value)
    {
        var index = varId - WorkBaseId;
        if ((uint)index >= (uint)WorkCount)
        {
            value = 0;
            return false;
        }

        value = _sav.GetWork(index);
        return true;
    }

    public void SetFlag(int flagId, bool value)
    {
        if ((uint)flagId >= (uint)FlagCount)
            throw HgssException.Precondition("flag-range", $"Flag id {flagId} is outside the persistent range 0..{FlagCount - 1}.");
        _sav.SetEventFlag(flagId, value);
    }

    public void SetVar(int varId, int value)
    {
        var index = varId - WorkBaseId;
        if ((uint)index >= (uint)WorkCount)
            throw HgssException.Precondition("variable-range", $"Variable id 0x{varId:X4} is not a save-work slot.");
        if ((uint)value > ushort.MaxValue)
            throw HgssException.Precondition("variable-range", $"Variable value {value} does not fit in 16 bits.");
        _sav.SetWork(index, (ushort)value);
    }

    public void SetMoney(uint money)
    {
        if (money > (uint)_sav.MaxMoney)
            throw HgssException.Precondition("money-range", $"Money {money} exceeds the HGSS maximum {_sav.MaxMoney}.");
        _sav.Money = money;
    }

    public bool TryGetBagQuantity(int itemId, out int quantity, out PouchKind pouch)
    {
        var bag = _sav.Inventory;
        foreach (var p in bag.Pouches)
        {
            if (!p.CanContain((ushort)itemId))
                continue;
            var kind = ToPouchKind(p.Type);
            var limit = HgssLayout.PhysicalSlots(kind);
            for (var i = 0; i < Math.Min(limit, p.Items.Length); i++)
            {
                if (p.Items[i].Index == itemId)
                {
                    quantity = p.Items[i].Count;
                    pouch = kind;
                    return true;
                }
            }
        }

        quantity = 0;
        pouch = PouchKind.Items;
        return false;
    }

    public void SetBagQuantity(int itemId, int quantity)
    {
        if (quantity < 0 || quantity > 999)
            throw HgssException.Precondition("item-quantity", $"Quantity {quantity} is outside 0..999.");
        var bag = _sav.Inventory;
        foreach (var p in bag.Pouches)
        {
            if (!p.CanContain((ushort)itemId))
                continue;
            var kind = ToPouchKind(p.Type);
            var limit = Math.Min(HgssLayout.PhysicalSlots(kind), p.Items.Length);
            var max = bag.GetMaxCount(p.Type, itemId);
            if (quantity > max)
            {
                throw HgssException.Precondition("item-quantity",
                    $"Quantity {quantity} exceeds the pocket maximum {max} for item {itemId}. Refusing to clamp.");
            }

            var slot = -1;
            for (var i = 0; i < limit; i++)
            {
                if (p.Items[i].Index == itemId)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0 && quantity > 0)
            {
                for (var i = 0; i < limit; i++)
                {
                    if (p.Items[i].Count == 0)
                    {
                        slot = i;
                        break;
                    }
                }
            }

            if (slot < 0)
            {
                if (quantity == 0)
                    return;
                throw HgssException.Precondition("item-space", $"No free slot for item {itemId} in pocket {kind}.");
            }

            p.Items[slot].Index = quantity == 0 ? 0 : itemId;
            p.Items[slot].Count = quantity;
            bag.CopyTo(_sav); // essential: Inventory constructs a detached copy (PlayerBag4HGSS.cs:26-31)
            return;
        }

        throw HgssException.Unsafe("item-pocket", $"Item {itemId} is not carried in any HGSS pocket.");
    }

    public IReadOnlyList<BagEntry> BagEntries()
    {
        var entries = new List<BagEntry>();
        var bag = _sav.Inventory;
        foreach (var p in bag.Pouches)
        {
            var kind = ToPouchKind(p.Type);
            var limit = Math.Min(HgssLayout.PhysicalSlots(kind), p.Items.Length);
            for (var i = 0; i < limit; i++)
            {
                var item = p.Items[i];
                if (item.Index == 0 && item.Count == 0)
                    continue;
                entries.Add(new BagEntry(kind, i, item.Index, HgssNames.ItemName(item.Index), item.Count));
            }
        }

        return entries;
    }

    public IReadOnlyList<int> SetFlagIds()
    {
        var set = new List<int>();
        for (var i = 0; i < FlagCount; i++)
        {
            if (_sav.GetEventFlag(i))
                set.Add(i);
        }

        return set;
    }

    public IReadOnlyDictionary<int, int> WorkValues()
    {
        var values = new Dictionary<int, int>();
        for (var i = 0; i < WorkCount; i++)
            values[WorkBaseId + i] = _sav.GetWork(i);

        return values;
    }

    public bool IsPocketableItem(int itemId)
    {
        if (itemId <= 0)
            return false;
        var bag = _sav.Inventory;
        return bag.Pouches.Any(p => p.CanContain((ushort)itemId));
    }

    public ExportOutcome Export()
    {
        var bytes = _sav.Write().ToArray();
        var reparsed = new SAV4HGSS(bytes);
        var info = reparsed.ChecksumInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new ExportOutcome(bytes, reparsed.ChecksumsValid, info);
    }

    public void Dispose()
    {
    }

    private IReadOnlyList<PartySlotInfo> PartySlots()
    {
        var slots = new List<PartySlotInfo>(6);
        for (var i = 0; i < 6; i++)
        {
            var pk = _sav.GetPartySlotAtIndex(i);
            if (pk.Species == 0)
            {
                slots.Add(new PartySlotInfo(i, false, false, 0, "", "", 0, 0, 0, null, 0, 0, 0, 0, 0, []));
                continue;
            }

            slots.Add(new PartySlotInfo(i, true, pk.IsEgg, pk.Species, HgssNames.ForSpecies(pk.Species),
                pk.Nickname, pk.Form, pk.CurrentLevel, pk.HeldItem, HgssNames.ItemName(pk.HeldItem),
                pk.Gender, pk.TID16, pk.SID16, (int)pk.EXP, pk.OriginalTrainerFriendship,
                [pk.Move1, pk.Move2, pk.Move3, pk.Move4]));
        }

        return slots;
    }

    private IReadOnlyList<PouchCapacity> PouchCapacities()
    {
        var bag = _sav.Inventory;
        return bag.Pouches.Select(p =>
        {
            var kind = ToPouchKind(p.Type);
            var limit = Math.Min(HgssLayout.PhysicalSlots(kind), p.Items.Length);
            var occupied = 0;
            for (var i = 0; i < limit; i++)
            {
                if (p.Items[i].Count > 0)
                    occupied++;
            }

            return new PouchCapacity(kind, HgssLayout.PhysicalSlots(kind), occupied, p.MaxCount);
        }).ToList();
    }

    private IReadOnlyList<BoxSlotInfo> PresentBoxSlots()
    {
        var present = new List<BoxSlotInfo>();
        for (var box = 0; box < _sav.BoxCount; box++)
        {
            for (var slot = 0; slot < _sav.BoxSlotCount; slot++)
            {
                var pk = _sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0)
                    continue;
                present.Add(new BoxSlotInfo(box, slot, true, pk.IsEgg, pk.Species,
                    HgssNames.ForSpecies(pk.Species), pk.Nickname, pk.CurrentLevel));
            }
        }

        return present;
    }

    private (int Seen, int Caught) DexCounts()
    {
        int seen = 0, caught = 0;
        for (ushort species = 1; species <= _sav.MaxSpeciesID; species++)
        {
            if (_sav.GetSeen(species))
                seen++;
            if (_sav.GetCaught(species))
                caught++;
        }

        return (seen, caught);
    }

    private int CountSetFlags()
    {
        var count = 0;
        for (var i = 0; i < FlagCount; i++)
        {
            if (_sav.GetEventFlag(i))
                count++;
        }

        return count;
    }

    private int CountNonZeroWork()
    {
        var count = 0;
        for (var i = 0; i < WorkCount; i++)
        {
            if (_sav.GetWork(i) != 0)
                count++;
        }

        return count;
    }

    internal static PouchKind ToPouchKind(InventoryType type) => type switch
    {
        InventoryType.Items => PouchKind.Items,
        InventoryType.KeyItems => PouchKind.KeyItems,
        InventoryType.TMHMs => PouchKind.Machines,
        InventoryType.MailItems => PouchKind.Mail,
        InventoryType.Medicine => PouchKind.Medicine,
        InventoryType.Berries => PouchKind.Berries,
        InventoryType.Balls => PouchKind.Balls,
        InventoryType.BattleItems => PouchKind.BattleItems,
        _ => throw HgssException.Internal("pouch-kind", $"Unexpected HGSS pocket {type}."),
    };
}
