using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HgssSave.Core;

public sealed record FlagChange(int Id, string? Name, bool Before, bool After)
{
    public string Direction => After ? "set" : "cleared";
}

public sealed record VarChange(int Id, string? Name, int Before, int After);

public sealed record BagChange(int ItemId, string? Name, PouchKind Pouch, int Before, int After);

public sealed record PartyChange(int Slot, string Field, string Before, string After);

public sealed record SaveDiffResult(
    string BeforePath,
    string BeforeSha256,
    SaveIdentity BeforeIdentity,
    string AfterPath,
    string AfterSha256,
    SaveIdentity AfterIdentity,
    IReadOnlyList<SemanticChange> Fields,
    int FlagsChanged,
    IReadOnlyList<FlagChange> Flags,
    int VarsChanged,
    IReadOnlyList<VarChange> Vars,
    IReadOnlyList<BagChange> Bag,
    IReadOnlyList<PartyChange> Party,
    ByteAuditResult Bytes,
    bool SameFile,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Semantic diff between two saves (design §9: emit a semantic diff and an annotated binary diff).
/// </summary>
public static class SaveDiff
{
    public const int DefaultListLimit = 200;

    /// <summary>Scalar field changes; flag, variable, bag and party changes are reported separately.</summary>
    public static IReadOnlyList<SemanticChange> FieldChanges(SaveSnapshot before, SaveSnapshot after)
    {
        var changes = new List<SemanticChange>();

        void Add(string field, object a, object b)
        {
            if (!Equals(a, b))
                changes.Add(new SemanticChange(field, a.ToString() ?? "", b.ToString() ?? ""));
        }

        Add("game", before.Identity.Game, after.Identity.Game);
        Add("language", before.Identity.Language, after.Identity.Language);
        Add("trainer.name", before.Trainer.Name, after.Trainer.Name);
        Add("trainer.gender", before.Trainer.Gender, after.Trainer.Gender);
        Add("trainer.money", before.Trainer.Money, after.Trainer.Money);
        Add("trainer.coins", before.Trainer.Coins, after.Trainer.Coins);
        Add("trainer.tid", before.Trainer.Tid16, after.Trainer.Tid16);
        Add("trainer.sid", before.Trainer.Sid16, after.Trainer.Sid16);
        Add("trainer.badges_johto", before.Trainer.JohtoBadges, after.Trainer.JohtoBadges);
        Add("trainer.badges_kanto", before.Trainer.KantoBadges, after.Trainer.KantoBadges);
        Add("trainer.played_hours", before.Trainer.PlayedHours, after.Trainer.PlayedHours);
        Add("trainer.played_minutes", before.Trainer.PlayedMinutes, after.Trainer.PlayedMinutes);
        Add("trainer.played_seconds", before.Trainer.PlayedSeconds, after.Trainer.PlayedSeconds);
        Add("location.map_id", before.Location.MapId, after.Location.MapId);
        Add("location.x", before.Location.X, after.Location.X);
        Add("location.y", before.Location.Y, after.Location.Y);
        Add("location.secondary_x", before.Location.SecondaryX, after.Location.SecondaryX);
        Add("location.secondary_y", before.Location.SecondaryY, after.Location.SecondaryY);
        Add("location.z", before.Location.Z, after.Location.Z);
        Add("dex.seen", before.Dex.Seen, after.Dex.Seen);
        Add("dex.caught", before.Dex.Caught, after.Dex.Caught);
        Add("party.count", before.Party.Count(p => p.Present), after.Party.Count(p => p.Present));
        Add("bag.entries", before.Bag.Count(e => e.Quantity > 0), after.Bag.Count(e => e.Quantity > 0));
        Add("flags.set", before.FlagsSet, after.FlagsSet);
        Add("variables.nonzero", before.WorkNonZero, after.WorkNonZero);
        Add("active_partition.general", before.Preflight.ActiveGeneral.ToString(), after.Preflight.ActiveGeneral.ToString());
        Add("active_partition.storage", before.Preflight.ActiveStorage.ToString(), after.Preflight.ActiveStorage.ToString());

        return changes;
    }

    public static SaveDiffResult Compare(
        ISaveSession before,
        ISaveSession after,
        SymbolRegistry registry,
        string beforePath,
        byte[] beforeBytes,
        string afterPath,
        byte[] afterBytes,
        IReadOnlyList<ByteRegion> regions,
        int maxList = DefaultListLimit,
        int maxRanges = 40)
    {
        var snapshotBefore = before.Snapshot();
        var snapshotAfter = after.Snapshot();
        var warnings = new List<string>();

        var flagsBefore = new HashSet<int>(before.SetFlagIds());
        flagsBefore.SymmetricExceptWith(after.SetFlagIds());
        var flagChanges = flagsBefore
            .OrderBy(id => id)
            .Select(id => new FlagChange(
                id,
                registry.NameFor(SymbolKind.PersistentFlag, id),
                before.TryGetFlag(id, out var wasSet) && wasSet,
                after.TryGetFlag(id, out var isSet) && isSet))
            .ToList();

        var varsBefore = before.WorkValues();
        var varsAfter = after.WorkValues();
        var varIds = varsBefore.Keys.Union(varsAfter.Keys).OrderBy(id => id).ToList();
        var varChanges = varIds
            .Select(id => (Id: id, Before: varsBefore.GetValueOrDefault(id, 0), After: varsAfter.GetValueOrDefault(id, 0)))
            .Where(v => v.Before != v.After)
            .Select(v => new VarChange(v.Id, registry.NameFor(SymbolKind.SaveWork, v.Id), v.Before, v.After))
            .ToList();

        var bagBefore = before.BagEntries().Where(e => e.Quantity > 0).ToDictionary(e => e.ItemId, e => e);
        var bagAfter = after.BagEntries().Where(e => e.Quantity > 0).ToDictionary(e => e.ItemId, e => e);
        var itemIds = bagBefore.Keys.Union(bagAfter.Keys).OrderBy(i => i).ToList();
        var bagChanges = itemIds
            .Select(id => (Id: id, Before: bagBefore.GetValueOrDefault(id), After: bagAfter.GetValueOrDefault(id)))
            .Where(e => (e.Before?.Quantity ?? 0) != (e.After?.Quantity ?? 0))
            .Select(e => new BagChange(
                e.Id,
                registry.NameFor(SymbolKind.Item, e.Id) ?? (e.Before?.ItemName ?? e.After?.ItemName),
                e.After?.Pouch ?? e.Before!.Pouch,
                e.Before?.Quantity ?? 0,
                e.After?.Quantity ?? 0))
            .ToList();

        var partyChanges = new List<PartyChange>();
        foreach (var (slotBefore, slotAfter) in snapshotBefore.Party.Zip(snapshotAfter.Party))
        {
            AddPartyChange(partyChanges, slotBefore.Index, "present", slotBefore.Present.ToString(), slotAfter.Present.ToString());
            if (!slotBefore.Present || !slotAfter.Present)
                continue;

            AddPartyChange(partyChanges, slotBefore.Index, "species", $"{slotBefore.SpeciesName} ({slotBefore.Species})", $"{slotAfter.SpeciesName} ({slotAfter.Species})");
            AddPartyChange(partyChanges, slotBefore.Index, "nickname", slotBefore.Nickname, slotAfter.Nickname);
            AddPartyChange(partyChanges, slotBefore.Index, "level", slotBefore.Level.ToString(CultureInfo.InvariantCulture), slotAfter.Level.ToString(CultureInfo.InvariantCulture));
            AddPartyChange(partyChanges, slotBefore.Index, "held_item", slotBefore.HeldItemName ?? slotBefore.HeldItem.ToString(CultureInfo.InvariantCulture), slotAfter.HeldItemName ?? slotAfter.HeldItem.ToString(CultureInfo.InvariantCulture));
            AddPartyChange(partyChanges, slotBefore.Index, "egg", slotBefore.IsEgg.ToString(), slotAfter.IsEgg.ToString());
        }

        var bytes = ByteAudit.ClassifyDiff(beforeBytes, afterBytes, regions, maxRanges);
        var sameFile = beforeBytes.AsSpan().SequenceEqual(afterBytes);

        if (snapshotBefore.Identity.Game != snapshotAfter.Identity.Game)
            warnings.Add($"the two saves identify different games ({snapshotBefore.Identity.Game} vs {snapshotAfter.Identity.Game}).");

        return new SaveDiffResult(
            beforePath,
            HashUtil.Sha256(beforeBytes),
            snapshotBefore.Identity,
            afterPath,
            HashUtil.Sha256(afterBytes),
            snapshotAfter.Identity,
            FieldChanges(snapshotBefore, snapshotAfter),
            flagChanges.Count,
            flagChanges.Take(maxList).ToList(),
            varChanges.Count,
            varChanges.Take(maxList).ToList(),
            bagChanges.Take(maxList).ToList(),
            partyChanges.Take(maxList).ToList(),
            bytes,
            sameFile,
            warnings);
    }

    private static void AddPartyChange(List<PartyChange> changes, int slot, string field, string before, string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
            changes.Add(new PartyChange(slot, field, before, after));
    }
}
