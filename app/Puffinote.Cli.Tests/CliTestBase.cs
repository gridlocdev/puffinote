using Puffinote.Cli;

namespace Puffinote.Cli.Tests;

/// <summary>
/// Shared harness for the router's end-to-end tests: drive <see cref="CliApplication.Run"/> in-process
/// with the process-global <see cref="Console"/> redirected, over a throwaway temp notebook. Both test
/// classes share the "cli" collection so they run sequentially (the Console redirect is global).
/// </summary>
public abstract class CliTestBase : IDisposable
{
    private readonly TextWriter _origOut = Console.Out;
    private readonly TextWriter _origErr = Console.Error;

    protected CliTestBase()
    {
        Notebook = Path.Combine(Path.GetTempPath(), $"puffinote-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Notebook);
    }

    /// <summary>The temp notebook root for this test.</summary>
    protected string Notebook { get; }

    public void Dispose()
    {
        Console.SetOut(_origOut);
        Console.SetError(_origErr);
        try
        {
            Directory.Delete(Notebook, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Write a file (creating parent folders) under the notebook and return its absolute path.</summary>
    protected string Write(string relativePath, string content)
    {
        var path = Path.Combine(Notebook, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Run the router in-process, capturing stdout/stderr and the exit code.</summary>
    protected static (int Code, string Out, string Err) Run(params string[] args)
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        Console.SetOut(outW);
        Console.SetError(errW);
        var code = CliApplication.Run(args);
        return (code, outW.ToString(), errW.ToString());
    }
}
