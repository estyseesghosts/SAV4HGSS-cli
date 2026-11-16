namespace HgssSave.Core;

/// <summary>
/// Process exit codes. Values and meanings are fixed by
/// hgss_save_cli_engineering_design.md §4 ("Exit codes").
/// </summary>
public enum ExitCode
{
    Success = 0,
    BadArguments = 2,
    UnsupportedSave = 3,
    StructuralFailure = 4,
    SymbolFailure = 5,
    PreconditionFailed = 6,
    OutputConflict = 7,
    UnsafeOperation = 8,
    InternalError = 9,
}
