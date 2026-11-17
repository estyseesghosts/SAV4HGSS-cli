using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HgssSave.Core;

/// <summary>
/// What the game itself changes when a badge is won (registry/hgss-us.badge-rules.json, built by
/// tools/make_badge_rules.py from the gym scripts and checked against played saves; docs/state-log.md F8).
/// <c>trainer.badges.set</c> with <c>consequences</c> expands to these writes for every badge newly gained,
/// plus counter rules such as VAR_MIDGAME_BADGES.
/// </summary>
public sealed class BadgeRules
{
    public const string SchemaId = "hgss-save.badge-rules/v1";
    public const string FileName = "hgss-us.badge-rules.json";

    public sealed record Effect(string Kind, string Name, int Id, int Value);

    public sealed record Counter(string Name, int VarId, int[] Badges, int HoldFrom);

    private readonly Dictionary<int, List<Effect>> _byBadge;

    private BadgeRules(Dictionary<int, List<Effect>> byBadge, List<Counter> counters, string path)
    {
        _byBadge = byBadge;
        Counters = counters;
        Path = path;
    }

    public string Path { get; }

    public IReadOnlyList<Counter> Counters { get; }

    public IReadOnlyList<Effect> EffectsFor(int badge) =>
        _byBadge.TryGetValue(badge, out var list) ? list : [];

    /// <summary>Badges are numbered 0..7 Johto, 8..15 Kanto (badge.h).</summary>
    public static int FullMask(int johto, int kanto) => (johto & 0xFF) | ((kanto & 0xFF) << 8);

    /// <summary>Operations a badge change implies, given the badge masks before and after and the live save.</summary>
    public IEnumerable<PatchOperation> Expand(ISaveSession session, int oldJohto, int oldKanto, int newJohto, int newKanto)
    {
        var before = FullMask(oldJohto, oldKanto);
        var after = FullMask(newJohto, newKanto);
        for (var badge = 0; badge < 16; badge++)
        {
            if (((before >> badge) & 1) != 0 || ((after >> badge) & 1) == 0)
                continue;
            foreach (var e in EffectsFor(badge))
            {
                switch (e.Kind)
                {
                    case "flag":
                        yield return new FlagSetOperation(e.Id.ToString(), e.Value != 0, null);
                        break;
                    case "var":
                        yield return new VariableSetOperation(e.Id.ToString(), e.Value, null);
                        break;
                    case "item":
                        if (!session.TryGetBagQuantity(e.Id, out var have, out _) || have == 0)
                            yield return new BagQuantitySetOperation(e.Id.ToString(), e.Value, null);
                        break;
                }
            }
        }

        foreach (var counter in Counters)
        {
            var changed = counter.Badges.Any(b => ((before >> b) & 1) != ((after >> b) & 1));
            if (!changed)
                continue;
            var count = counter.Badges.Count(b => ((after >> b) & 1) != 0);
            var value = count;
            if (count >= counter.HoldFrom && session.TryGetVar(counter.VarId, out var current))
                value = Math.Max(count, current);
            yield return new VariableSetOperation(counter.VarId.ToString(), value, null);
        }
    }

    public sealed record Violation(string Rule, string Symbol, int Id, string Expected, string Actual, string Because);

    /// <summary>
    /// Compares a save against the rules: every owned badge's effects must hold, and each counter must agree with the
    /// badge byte. A violation means the save has a badge without what the game sets when it is won (or the reverse for
    /// counters), which a played save never shows (state-log F8).
    /// </summary>
    public IReadOnlyList<Violation> Audit(ISaveSession session)
    {
        var trainer = session.Trainer;
        var mask = FullMask(trainer.JohtoBadges, trainer.KantoBadges);
        var result = new List<Violation>();
        for (var badge = 0; badge < 16; badge++)
        {
            if (((mask >> badge) & 1) == 0)
                continue;
            foreach (var e in EffectsFor(badge))
            {
                var because = $"badge {badge} owned";
                switch (e.Kind)
                {
                    case "flag":
                        if (!session.TryGetFlag(e.Id, out var flag) || flag != (e.Value != 0))
                            result.Add(new Violation("badge-effect", e.Name, e.Id, (e.Value != 0).ToString().ToLowerInvariant(), (!(e.Value != 0)).ToString().ToLowerInvariant(), because));
                        break;
                    case "var":
                        if (!session.TryGetVar(e.Id, out var v) || v != e.Value)
                            result.Add(new Violation("badge-effect", e.Name, e.Id, e.Value.ToString(), session.TryGetVar(e.Id, out var actual) ? actual.ToString() : "unreadable", because));
                        break;
                    // Items are not audited: Gen 4 TMs are consumed on use, and 10 of 29 played saves own the badge without the TM.
                }
            }
        }

        foreach (var counter in Counters)
        {
            var count = counter.Badges.Count(b => ((mask >> b) & 1) != 0);
            if (!session.TryGetVar(counter.VarId, out var value))
                continue;
            var ok = count < counter.HoldFrom ? value == count : value >= count;
            if (!ok)
                result.Add(new Violation("counter", counter.Name, counter.VarId,
                    count < counter.HoldFrom ? count.ToString() : $">= {count}", value.ToString(),
                    $"{count} of badges [{string.Join(",", counter.Badges)}] owned"));
        }

        return result;
    }

    public static BadgeRules LoadDefault(string? explicitPath = null)
    {
        var path = explicitPath ?? Environment.GetEnvironmentVariable("HGSS_BADGE_RULES") ?? FindDefault();
        if (path is null || !File.Exists(path))
            throw HgssException.BadArguments("badge-rules-missing",
                $"Badge rules not found ({path ?? "(null)"}). Pass --badge-rules PATH or set HGSS_BADGE_RULES, or use --raw to write only the badge bytes.");
        return Load(path);
    }

    private static string? FindDefault()
    {
        var candidates = new List<string>();
        var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null)
            candidates.Add(System.IO.Path.Combine(exeDir, FileName));
        var cwd = Directory.GetCurrentDirectory();
        candidates.Add(System.IO.Path.Combine(cwd, FileName));
        candidates.Add(System.IO.Path.Combine(cwd, "registry", FileName));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static BadgeRules Load(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = doc.RootElement;
            if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != SchemaId)
                throw HgssException.BadArguments("badge-rules-schema", $"Badge rules '{path}' must declare schema '{SchemaId}'.");
            var byBadge = new Dictionary<int, List<Effect>>();
            foreach (var badge in root.GetProperty("badges").EnumerateArray())
            {
                var list = new List<Effect>();
                foreach (var e in badge.GetProperty("effects").EnumerateArray())
                {
                    var kind = e.GetProperty("kind").GetString()!;
                    var value = kind switch
                    {
                        "flag" => e.GetProperty("value").GetBoolean() ? 1 : 0,
                        "var" => e.GetProperty("value").GetInt32(),
                        "item" => e.GetProperty("quantity").GetInt32(),
                        _ => throw new FormatException($"unknown effect kind '{kind}'"),
                    };
                    list.Add(new Effect(kind, e.GetProperty("name").GetString()!, e.GetProperty("id").GetInt32(), value));
                }

                byBadge[badge.GetProperty("id").GetInt32()] = list;
            }

            var counters = new List<Counter>();
            foreach (var c in root.GetProperty("counters").EnumerateArray())
                counters.Add(new Counter(c.GetProperty("var").GetString()!, c.GetProperty("id").GetInt32(),
                    c.GetProperty("badges").EnumerateArray().Select(x => x.GetInt32()).ToArray(), c.GetProperty("hold_from").GetInt32()));
            return new BadgeRules(byBadge, counters, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw HgssException.BadArguments("badge-rules-io", $"Cannot read badge rules '{path}': {e.Message}");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw HgssException.BadArguments("badge-rules-parse", $"Badge rules '{path}' are not valid: {e.Message}");
        }
    }
}
