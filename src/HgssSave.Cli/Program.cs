using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HgssSave.Core;
using HgssSave.PKHeX;

namespace HgssSave.Cli;

internal static class Program
{
    internal const string CliVersion = "0.1.0";

    private static readonly string[] CommandList =
    [
        "version", "inspect", "validate", "diff", "patch",
        "flag get", "flag set", "var get", "var set", "bag set", "trainer money set",
        "party export", "party import", "party clear",
        "box export", "box import", "box clear",
        "location set", "dex set",
        "flags list", "vars list",
        "symbols lookup", "symbols search", "symbols build-registry",
        "script index", "script references",
        "fixture build", "fixture verify",
        "container probe", "container unwrap", "blank",
    ];

    internal static IReadOnlyList<string> CommandNames => CommandList;

    internal static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
            return Usage(null);

        try
        {
            return Dispatch(args);
        }
        catch (HgssException e)
        {
            EmitError(e);
            return (int)e.ExitCode;
        }
        catch (Exception e)
        {
            var wrapped = HgssException.Internal("unhandled", e.Message);
            EmitError(wrapped);
            return (int)ExitCode.InternalError;
        }
    }

    private static int Dispatch(string[] args)
    {
        var cmd = args[0].ToLowerInvariant();
        var rest = args[1..];
        return cmd switch
        {
            "version" => Commands.Version(rest),
            "inspect" => Commands.Inspect(rest),
            "validate" => Commands.Validate(rest),
            "diff" => Commands.Diff(rest),
            "patch" => Commands.Patch(rest),
            "flag" => FlagVerb(rest),
            "var" => VarVerb(rest),
            "bag" => BagVerb(rest),
            "trainer" => TrainerVerb(rest),
            "flags" => ExpectList(rest, "flags") ? Commands.FlagsList(rest[1..]) : Usage("usage: hgss-save flags list INPUT [--limit N]"),
            "vars" => ExpectList(rest, "vars") ? Commands.VarsList(rest[1..]) : Usage("usage: hgss-save vars list INPUT [--limit N]"),
            "party" => PartyVerb(rest),
            "box" => BoxVerb(rest),
            "location" => LocationVerb(rest),
            "dex" => DexVerb(rest),
            "script" => ScriptVerb(rest),
            "fixture" => FixtureVerb(rest),
            "symbols" => SymbolsVerb(rest),
            "container" => ContainerVerb(rest),
            "blank" => Commands.Blank(rest),
            _ => Usage($"unknown command '{args[0]}'"),
        };
    }

    private static int FlagVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save flag get|set ...");
        return args[0].ToLowerInvariant() switch
        {
            "get" => Commands.FlagGet(args[1..]),
            "set" => Commands.FlagSet(args[1..]),
            _ => Usage("usage: hgss-save flag get|set ..."),
        };
    }

    private static int VarVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save var get|set ...");
        return args[0].ToLowerInvariant() switch
        {
            "get" => Commands.VarGet(args[1..]),
            "set" => Commands.VarSet(args[1..]),
            _ => Usage("usage: hgss-save var get|set ..."),
        };
    }

    private static int BagVerb(string[] args) =>
        args.Length > 0 && args[0].ToLowerInvariant() == "set"
            ? Commands.BagSet(args[1..])
            : Usage("usage: hgss-save bag set INPUT ITEM QUANTITY --out OUT [--expect N]");

    private static int PartyVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save party export|import|clear ...");
        return args[0].ToLowerInvariant() switch
        {
            "export" => Commands.PartyExport(args[1..]),
            "import" => Commands.PartyImport(args[1..]),
            "clear" => Commands.PartyClear(args[1..]),
            _ => Usage("usage: hgss-save party export|import|clear ..."),
        };
    }

    private static int BoxVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save box export|import|clear ...");
        return args[0].ToLowerInvariant() switch
        {
            "export" => Commands.BoxExport(args[1..]),
            "import" => Commands.BoxImport(args[1..]),
            "clear" => Commands.BoxClear(args[1..]),
            _ => Usage("usage: hgss-save box export|import|clear ..."),
        };
    }

    private static int LocationVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save location set ...");
        return args[0].ToLowerInvariant() switch
        {
            "set" => Commands.LocationSet(args[1..]),
            _ => Usage("usage: hgss-save location set ..."),
        };
    }

    private static int DexVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save dex set ...");
        return args[0].ToLowerInvariant() switch
        {
            "set" => Commands.DexSet(args[1..]),
            _ => Usage("usage: hgss-save dex set ..."),
        };
    }

    private static int TrainerVerb(string[] args) =>
        args.Length > 1 && args[0].ToLowerInvariant() == "money" && args[1].ToLowerInvariant() == "set"
            ? Commands.MoneySet(args[2..])
            : Usage("usage: hgss-save trainer money set INPUT VALUE --out OUT [--expect N]");

    private static int SymbolsVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save symbols lookup|search|build-registry ...");
        return args[0].ToLowerInvariant() switch
        {
            "lookup" => Commands.SymbolsLookup(args[1..]),
            "search" => Commands.SymbolsSearch(args[1..]),
            "build-registry" => Commands.BuildRegistry(args[1..]),
            _ => Usage("usage: hgss-save symbols lookup|search|build-registry ..."),
        };
    }

    private static int ScriptVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save script index|references ...");
        return args[0].ToLowerInvariant() switch
        {
            "index" => Commands.ScriptIndex(args[1..]),
            "references" => Commands.ScriptReferences(args[1..]),
            _ => Usage("usage: hgss-save script index|references ..."),
        };
    }

    private static int FixtureVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save fixture build|verify ...");
        return args[0].ToLowerInvariant() switch
        {
            "build" => Commands.FixtureBuild(args[1..]),
            "verify" => Commands.FixtureVerify(args[1..]),
            _ => Usage("usage: hgss-save fixture build|verify ..."),
        };
    }

    private static int ContainerVerb(string[] args)
    {
        if (args.Length == 0)
            return Usage("usage: hgss-save container probe|unwrap ...");
        return args[0].ToLowerInvariant() switch
        {
            "probe" => Commands.ContainerProbe(args[1..]),
            "unwrap" => Commands.ContainerUnwrap(args[1..]),
            _ => Usage("usage: hgss-save container probe|unwrap ..."),
        };
    }

    private static bool ExpectList(string[] args, string what) =>
        args.Length > 0 && args[0].ToLowerInvariant() == "list";

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "help";

    internal static int Usage(string? message)
    {
        if (message is not null)
            Console.Error.WriteLine(message);
        Console.Error.WriteLine("hgss-save: HGSS raw-save inspector and transactional patcher (PKHeX.Core engine).");
        Console.Error.WriteLine("See README.md for the full command reference.");
        return (int)ExitCode.BadArguments;
    }

    private static void EmitError(HgssException e)
    {
        Console.Out.WriteLine(ReportWriter.Serialize(w => ReportWriter.WriteError(w, e), indent: true));
        Console.Error.WriteLine($"error [{e.DiagnosticId}]: {e.Message}");
    }
}

/// <summary>Minimal flag/option parser: --name value, --name=value, boolean --flag.</summary>
internal static class Args
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--recipe", "--out", "--outdir", "--bases", "--symbols", "--safe-spawns", "--seen", "--caught", "--script-refs", "--kind", "--kinds", "--limit", "--level",
        "--game", "--trainer-name", "--tid", "--sid", "--gender", "--language",
        "--headers", "--scripts", "--commit", "--registry-id", "--project", "--max-list",
        "--flags-limit", "--expect", "--expect-species", "--max-ranges",
    };

    internal static string? Option(string[] args, string name, string? @default = null)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                return args[i][(name.Length + 1)..];
        }

        return @default;
    }

    internal static bool Flag(string[] args, string name)
    {
        foreach (var arg in args)
        {
            if (arg == name)
                return true;
        }

        return false;
    }

    internal static List<string> Positionals(string[] args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (arg.Contains('=', StringComparison.Ordinal))
                    continue;
                if (ValueOptions.Contains(arg))
                    i++; // skip the option's value
                continue;
            }

            result.Add(arg);
        }

        return result;
    }

    internal static string? Positional(string[] args, int index)
    {
        var all = Positionals(args);
        return index < all.Count ? all[index] : null;
    }
}
