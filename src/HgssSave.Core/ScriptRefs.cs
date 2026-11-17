using System.Collections.Generic;

namespace HgssSave.Core;

/// <summary>One script reference to a named symbol (design §7 script indexing).</summary>
public sealed record ScriptRef(
    string Symbol,
    string Access,
    string Command,
    string File,
    int Line,
    string? Context,
    string Confidence);

public static class ScriptAccess
{
    public const string Read = "read";
    public const string Write = "write";
    public const string ItemCheck = "check";
    public const string Marker = "marker";
}
