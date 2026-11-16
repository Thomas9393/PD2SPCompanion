using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using PD2SPCompanion.Core.Diagnostics;
using PD2SPCompanion.Core.Storage;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;

namespace PD2SPCompanion.Shell.Desktop
{
    internal class Program
    {
        /// <summary>
        /// The name the one running editor answers to. Held for the life of the process and
        /// released by the operating system when it ends, so a crash cannot leave the name
        /// taken and lock the editor out of starting again.
        /// </summary>
        private const string OnlyInstance = @"Local\PD2SPCompanion.single";

        /// <summary>The application's name, where Windows or the log shows it (AppVersion's).</summary>
        private const string AppName = PD2SPCompanion.Core.AppVersion.AppName;

        private static Mutex? _claim;

        /// <summary>A first-chance exception is being written to the log on this thread: what the writing throws is not written.</summary>
        [ThreadStatic] private static bool _logging;

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        /// <summary>
        /// Where the editor writes what killed it: <c>Data\Logs</c> beside the program, with
        /// everything else it keeps (EditorData), so a report means sending one folder's file.
        /// </summary>
        private static string CrashLog => Path.Combine(EditorData.Logs, "crash.log");

        /// <summary>
        /// Writes down what went wrong before the process goes. An editor that vanishes
        /// leaves whoever was using it with nothing to report and nothing to read, and the
        /// operating system's own record says only the exception's name. In the log too, after
        /// whatever led up to it; crash.log stays, on its own, the one file to ask for.
        /// </summary>
        private static void Record(object? error)
        {
            Log.Fatal("crash", error);

            try
            {
                string path = CrashLog;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // Held to the editor log's size, the older set aside (crash.1.log): it grew without end.
                Log.RollOver(path);

                File.AppendAllText(
                    path,
                    $"{Environment.NewLine}=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}"
                    + $"{error}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Nothing to be done about a log that cannot be written.
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            // Two editors open on one character is a way to lose work: each holds its own
            // copy of the save, and whichever writes second overwrites the other without
            // either knowing. The second one raises the first and stands down.
            _claim = new Mutex(initiallyOwned: true, OnlyInstance, out bool first);

            if (!first)
            {
                RaiseTheOther();
                return;
            }

            // The editor is a folder: it writes beside itself or not at all.
            if (EditorData.Prepare() is { } refused)
            {
                if (OperatingSystem.IsWindows())
                    MessageBox(IntPtr.Zero, refused, AppName, 0x10);
                return;
            }

            StartLog();

            // The game's tables, begun on a worker before anything else is: read beside the whole of the
            // program's own start (GameAhead).
            PD2SPCompanion.Shell.Services.Character.GameAhead.Begin();

            AppDomain.CurrentDomain.UnhandledException += (_, killed) => Record(killed.ExceptionObject);

            // PD2SPC_FIRST_CHANCE=1: every exception as it is thrown, caught or not, into the editor's log with
            // where it was thrown from. For a fault something swallows - UI Automation answers a provider's
            // exception with a bare E_FAIL (the vault's name box). Off unless asked for: it is loud.
            if (Environment.GetEnvironmentVariable("PD2SPC_FIRST_CHANCE") == "1")
            {
                AppDomain.CurrentDomain.FirstChanceException += (_, thrown) =>
                {
                    if (_logging)
                        return;

                    _logging = true;
                    try
                    {
                        Log.Warning("first chance", $"{thrown.Exception.GetType().FullName}: {thrown.Exception.Message}"
                            + Environment.NewLine + new StackTrace(1, false));
                    }
                    catch (Exception)
                    {
                        // The log itself failing must not become the fault being looked for.
                    }
                    finally
                    {
                        _logging = false;
                    }
                };
            }

            TaskScheduler.UnobservedTaskException += (_, ignored) =>
            {
                Record(ignored.Exception);
                ignored.SetObserved();
            };

            try
            {
                var appBuilder = BuildAvaloniaApp();

                int code = appBuilder.StartWithClassicDesktopLifetime(args);

                // Closed the ordinary way: said, so a log that ends without it is a process that vanished - one
                // closed under the player with no crash written and no line here (seen once: the log ended on the last
                // thing done).
                Log.Info("Stop", $"Closed (exit code {code}).");
            }
            catch (Exception fatal)
            {
                Record(fatal);
                throw;
            }
            finally
            {
                _claim.ReleaseMutex();
                _claim.Dispose();
            }
        }

        /// <summary>
        /// Opens the editor's log beside crash.log, headed with what is running: the editor's
        /// version and the commit it was built from, the system, and whether it is on test copies.
        /// <c>PD2SPC_LOG</c> puts it elsewhere: the UI tests keep theirs in the test pad.
        /// </summary>
        private static void StartLog()
        {
            string version = PD2SPCompanion.Core.AppVersion.Informational;
            bool testCopy = Environment.GetEnvironmentVariable("PD2SPC_SAVES") is { Length: > 0 };

            Log.Open(
                Environment.GetEnvironmentVariable("PD2SPC_LOG") is { Length: > 0 } elsewhere
                    ? elsewhere
                    : Path.Combine(EditorData.Logs, "editor.log"),
                $"{AppName} {version}; {RuntimeInformation.OSDescription}; .NET {Environment.Version}"
                + (testCopy ? "; TEST COPY" : ""));
        }

        /// <summary>
        /// Brings the editor that is already running to the front, so starting a second one
        /// looks like asking for the first rather than like nothing happening at all.
        /// </summary>
        private static void RaiseTheOther()
        {
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                using var self = Process.GetCurrentProcess();

                foreach (var other in Process.GetProcessesByName(self.ProcessName))
                {
                    using (other)
                    {
                        if (other.Id == self.Id || other.MainWindowHandle == IntPtr.Zero)
                            continue;

                        // Restore first: a minimised window accepts the foreground call and
                        // stays in the taskbar, which reads as the click doing nothing.
                        ShowWindow(other.MainWindowHandle, RestoreWindow);
                        SetForegroundWindow(other.MainWindowHandle);
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // Nothing to raise, or no permission to. Standing down quietly is still the
                // right outcome - the point was to not open a second editor.
            }
        }

        private const int RestoreWindow = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int how);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new SkiaOptions { MaxGpuResourceSizeBytes = 8096000 })
                .WithInterFont()
                .LogToTrace();
        }
    }
}
