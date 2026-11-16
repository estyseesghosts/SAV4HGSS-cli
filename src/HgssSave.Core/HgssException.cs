using System;

namespace HgssSave.Core;

/// <summary>
/// A typed application failure. Carries the exit code to surface and a stable
/// diagnostic identifier so reports and tests can assert on the cause rather than
/// on message text (design §4: exit codes are stable; design §12: deterministic output).
/// </summary>
public sealed class HgssException : Exception
{
    public ExitCode ExitCode { get; }

    public string DiagnosticId { get; }

    public HgssException(ExitCode exitCode, string diagnosticId, string message)
        : base(message)
    {
        ExitCode = exitCode;
        DiagnosticId = diagnosticId;
    }

    public static HgssException BadArguments(string diagnosticId, string message) =>
        new(ExitCode.BadArguments, diagnosticId, message);

    public static HgssException UnsupportedSave(string diagnosticId, string message) =>
        new(ExitCode.UnsupportedSave, diagnosticId, message);

    public static HgssException Structural(string diagnosticId, string message) =>
        new(ExitCode.StructuralFailure, diagnosticId, message);

    public static HgssException Symbol(string diagnosticId, string message) =>
        new(ExitCode.SymbolFailure, diagnosticId, message);

    public static HgssException Precondition(string diagnosticId, string message) =>
        new(ExitCode.PreconditionFailed, diagnosticId, message);

    public static HgssException OutputConflict(string diagnosticId, string message) =>
        new(ExitCode.OutputConflict, diagnosticId, message);

    public static HgssException Unsafe(string diagnosticId, string message) =>
        new(ExitCode.UnsafeOperation, diagnosticId, message);

    public static HgssException Internal(string diagnosticId, string message) =>
        new(ExitCode.InternalError, diagnosticId, message);
}
