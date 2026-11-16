using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace PD2SPCompanion.Core.Diagnostics;

/// <summary>
/// The editor's log: what went wrong and what was refused, kept in a file so that a reported
/// problem can be read rather than reproduced.
/// <para>
/// Off until <see cref="Open"/> names a file, so the CLI and the tests write nothing. The innermost
/// layer, so any layer may log (tests/layers.py).
/// </para>
/// <para>
/// Names and outcomes only - never an item's bytes or a save's contents: a report needs to say what
/// happened, not carry the player's items.
/// </para>
/// </summary>
public static class Log
{
    public enum Level { Info, Warning, Error, Fatal }

    /// <summary>The size a file grows to before it is set aside and a new one begun.</summary>
    public const long Cap = 1_000_000;

    /// <summary>How many set-aside files are kept beside the current one (editor.1.log, editor.2.log).</summary>
    public const int Kept = 2;

    private static readonly object Gate = new();

    /// <summary>The file being written, or null while the log is off.</summary>
    public static string? FilePath { get; private set; }

    /// <summary>
    /// Starts logging to <paramref name="path"/>, and writes a header: the session's start and
    /// <paramref name="header"/> - what is running (the editor's version and commit, the system).
    /// </summary>
    public static void Open(string path, string header)
    {
        lock (Gate)
        {
            FilePath = path;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }
            catch (Exception)
            {
                FilePath = null;
                return;
            }
        }

        Write($"{Environment.NewLine}=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {header}");
    }

    /// <summary>Stops logging; what was written stays.</summary>
    public static void Close()
    {
        lock (Gate)
            FilePath = null;
    }

    public static void Info(string area, string text) => Entry(Level.Info, area, text, null);

    public static void Warning(string area, string text, Exception? problem = null) => Entry(Level.Warning, area, text, problem);

    public static void Error(string area, string text, Exception? problem = null) => Entry(Level.Error, area, text, problem);

    /// <summary>What ended the process: an exception, or whatever the runtime handed over.</summary>
    public static void Fatal(string area, object? problem) =>
        Entry(Level.Fatal, area, problem as string ?? (problem as Exception)?.Message ?? "unknown", problem as Exception);

    /// <summary>
    /// An exception the editor caught and survived: it said something on screen, and this keeps
    /// the rest - the type, the stack - under the class and member that caught it. The first line of
    /// every catch that holds its exception.
    /// </summary>
    public static void Caught(Exception problem, [CallerMemberName] string member = "", [CallerFilePath] string file = "") =>
        Entry(Level.Warning, $"{Path.GetFileNameWithoutExtension(file)}.{member}", problem.Message, problem);

    private static void Entry(Level level, string area, string text, Exception? problem)
    {
        if (FilePath is null)
            return;

        string line = $"{DateTime.Now:HH:mm:ss.fff} {Label(level)} [{area}] {text}";
        if (problem is not null)
            line += Environment.NewLine + Indent(problem.ToString());

        Write(line);
    }

    private static string Label(Level level) => level switch
    {
        Level.Info => "INFO ",
        Level.Warning => "WARN ",
        Level.Error => "ERROR",
        _ => "FATAL",
    };

    private static string Indent(string text) =>
        "    " + text.Replace(Environment.NewLine, Environment.NewLine + "    ");

    private static void Write(string line)
    {
        lock (Gate)
        {
            if (FilePath is not { } path)
                return;

            try
            {
                RollOver(path);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // A log that cannot be written must not take the editor down with it.
            }
        }
    }

    /// <summary>
    /// Past <see cref="Cap"/>, the file is set aside as editor.1.log, and the oldest beyond <see cref="Kept"/> goes.
    /// Any log's: the crash log is held to the same size, or it would grow without end.
    /// </summary>
    public static void RollOver(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < Cap)
            return;

        string Aside(int n) => Path.Combine(info.DirectoryName!, $"{Path.GetFileNameWithoutExtension(path)}.{n}{info.Extension}");

        File.Delete(Aside(Kept));
        for (int n = Kept - 1; n >= 1; n--)
        {
            if (File.Exists(Aside(n)))
                File.Move(Aside(n), Aside(n + 1));
        }

        File.Move(path, Aside(1));
    }
}
