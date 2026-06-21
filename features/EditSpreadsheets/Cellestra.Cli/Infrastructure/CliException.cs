namespace Cellestra.Cli.Infrastructure;

/// <summary>
/// A user-facing error: its message is printed to stderr and the process exits non-zero.
/// Thrown for bad input, missing files, unknown sheets, etc. — never for a bug.
/// </summary>
public sealed class CliException(string message) : Exception(message);
