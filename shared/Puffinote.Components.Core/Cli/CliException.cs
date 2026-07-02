namespace Puffinote.Components.Core.Cli;

/// <summary>
/// A user-facing error: its message is printed to stderr and the process exits non-zero. Thrown for bad
/// input, missing files, unknown sheets, etc. — never for a bug. Shared by every CLI in the Puffinote
/// family (both apps had a byte-identical copy).
/// </summary>
public sealed class CliException(string message) : Exception(message);
