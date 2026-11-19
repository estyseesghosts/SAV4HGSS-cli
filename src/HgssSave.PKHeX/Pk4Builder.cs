using System;
using System.Collections.Generic;
using System.Linq;
using HgssSave.Core;
using PKHeX.Core;

namespace HgssSave.PKHeX;

/// <summary>
/// Builds the party-ready <c>.pk4</c> that runtime staging needs, so the emulator harness does not
/// have to carry its own PKHeX project (the one that lived in <c>%TEMP%</c> before this).
/// </summary>
/// <remarks>
/// This is harness tooling, not a legality claim. Trainer ids, IVs/EVs, ball and met location are
/// fixed, the species is any 1..493, and the default moveset comes from the game's own level-up
/// table. Recipes and <c>party import</c> still take exported templates; nothing here writes a save.
/// </remarks>
public static class Pk4Builder
{
    /// <summary>PK4 party form (<c>PK4.SIZE_PARTY</c> / <c>PokeCrypto.SIZE_4PARTY</c>, both 236).</summary>
    public const int PartyBytes = 236;

    private const int MaxSpecies = 493; // Legal.MaxSpeciesID_4, internal to PKHeX.Core.
    private const int MaxMove = 467;    // Legal.MaxMoveID_4, likewise.
    /// <summary>
    /// PK4's two name fields are sized differently. `OriginalTrainerName` passes 7 to
    /// StringConverter4.SetString and `Nickname` passes 10 (PKHeX.Core PKM/PK4.cs:242-247), and
    /// SetString hard-truncates past that count rather than rejecting, so anything longer would be
    /// silently lossy.
    /// </summary>
    private const int MaxOtLength = 7;

    private const int MaxNicknameLength = 10;

    public static (byte[] Bytes, PartyCreateResult Result) Create(
        int species,
        int level,
        IReadOnlyList<int>? moves,
        string originalTrainer,
        int tid,
        int sid,
        int language,
        string? nickname)
    {
        if (species is < 1 or > MaxSpecies)
            throw HgssException.BadArguments("species-range", $"Species {species} is outside the HGSS party range 1..{MaxSpecies}.");
        if (level is < 1 or > 100)
            throw HgssException.BadArguments("level-range", $"Level {level} is outside 1..100.");
        if (language is < 1 or > 8)
            throw HgssException.BadArguments("language-range", $"Language {language} is outside 1..8 (2 is English).");
        if (tid is < 0 or > 0xFFFF || sid is < 0 or > 0xFFFF)
            throw HgssException.BadArguments("id-range", $"TID/SID {tid}/{sid} must each fit in 16 bits.");
        CheckName(originalTrainer, "OT", MaxOtLength);
        if (nickname is not null)
            CheckName(nickname, "nickname", MaxNicknameLength);
        if (moves is { Count: > 4 })
            throw HgssException.BadArguments("moves-range", $"A PK4 holds four moves; got {moves.Count}.");

        var explicitMoves = moves is { Count: > 0 };
        var slots = new ushort[4];
        if (explicitMoves)
        {
            for (var i = 0; i < moves!.Count; i++)
            {
                var move = moves[i];
                if (move is < 0 or > MaxMove)
                    throw HgssException.BadArguments("moves-range", $"Move {move} is outside the HGSS range 0..{MaxMove}.");
                slots[i] = (ushort)move;
            }
        }
        else
        {
            // The species' own level-up moves at that level: the last four entries at or below it.
            // Typhlosion's fixed set was the old harness default and silently gave every other
            // species a Fire moveset (move slot 1 is what a bare A press selects).
            var learn = LearnSource4HGSS.Instance.GetLearnset((ushort)species, 0);
            learn.SetEncounterMoves((byte)level, slots, 0);
        }

        var pk = new PK4
        {
            Species = (ushort)species,
            CurrentLevel = (byte)level,
            TID16 = (ushort)tid,
            SID16 = (ushort)sid,
            Language = language,
            Ball = 4,          // Poké Ball.
            MetLevel = (byte)level,
            MetLocation = 4,   // A plain DP met location; this entity is not a legality claim.
            OriginalTrainerGender = 0,
            IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
            EV_HP = 6, EV_ATK = 252, EV_SPE = 252,
            Move1 = slots[0], Move2 = slots[1], Move3 = slots[2], Move4 = slots[3],
            OriginalTrainerName = originalTrainer,
        };

        // A zeroed nickname renders as garbage in the battle message box and a wrong one shows the
        // previous species' name, so the field must hold the species name while IsNicknamed is false.
        pk.Nickname = nickname ?? SpeciesName.GetSpeciesNameGeneration((ushort)species, language, 4);
        pk.IsNicknamed = nickname is not null;

        pk.RefreshChecksum();
        pk.ResetPartyStats(); // Party-only fields (level, HP, stats) off the stored block.
        pk.Heal();
        pk.RefreshChecksum();

        if (!pk.ChecksumValid)
            throw HgssException.Internal("pk-checksum", "The generated PK4 fails its own checksum.");

        var bytes = pk.Data.ToArray();
        if (bytes.Length != PartyBytes)
            throw HgssException.Internal("pk-size", $"Generated {bytes.Length} bytes; the party form is {PartyBytes}.");

        // No successful create may silently change a submitted name: PK4 stores both as UTF-16 and
        // the getter decodes what the setter wrote, so a mismatch means the value was lossy.
        if (!string.Equals(pk.OriginalTrainerName, originalTrainer, StringComparison.Ordinal))
            throw HgssException.BadArguments("name-roundtrip",
                $"The OT '{originalTrainer}' did not survive PK4 serialization (read back as '{pk.OriginalTrainerName}').");
        if (nickname is not null && !string.Equals(pk.Nickname, nickname, StringComparison.Ordinal))
            throw HgssException.BadArguments("name-roundtrip",
                $"The nickname '{nickname}' did not survive PK4 serialization (read back as '{pk.Nickname}').");

        var moveIds = slots.Select(m => (int)m).ToArray();
        var result = new PartyCreateResult(
            Species: species,
            SpeciesName: HgssNames.ForSpecies((ushort)species),
            Level: level,
            Hp: pk.Stat_HPMax,
            ChecksumValid: pk.ChecksumValid,
            FileBytes: bytes.Length,
            Moves: moveIds,
            MoveNames: moveIds.Select(id => HgssNames.ForMove((ushort)id)).ToArray(),
            OriginalTrainer: originalTrainer,
            Tid: tid,
            Sid: sid,
            Language: language,
            Nicknamed: pk.IsNicknamed,
            MovesSource: explicitMoves ? "explicit" : "level-up");

        return (bytes, result);
    }

    private static void CheckName(string name, string what, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw HgssException.BadArguments("name-empty", $"The {what} must not be empty (an empty one renders as garbage).");
        // Gen 4 stores UTF-16 code units and SetString counts chars, so a surrogate pair costs two.
        if (name.Length > maxLength)
            throw HgssException.BadArguments("name-length",
                $"The {what} '{name}' is {name.Length} UTF-16 units; gen 4 stores at most {maxLength} and truncates beyond that.");
        if (name.Any(char.IsSurrogate))
            throw HgssException.BadArguments("name-encoding",
                $"The {what} '{name}' contains a surrogate pair, which gen 4 cannot encode.");
    }
}
