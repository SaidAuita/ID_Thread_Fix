using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ID_Thread_Fix
{
    static class Program
    {
        private const string AppVersion = "1.0.0";
        private const string AppName = "InDesign Thread Fix (ID_Thread_Fix)";
        private const uint THREAD_TERMINATE = 0x0001;
        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;

        #region Win32 API

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateThread(IntPtr hThread, uint dwExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        #endregion

        private static bool _verbose;
        private static bool _silent;
        private static string _logFilePath;
        private static bool _hasConsole;

        [STAThread]
        static int Main(string[] args)
        {
            List<string> forwardedArgs = new List<string>();
            bool fixOnly = false;
            bool monitorMode = false;
            int monitorIntervalMinutes = 5;
            bool showHelp = false;
            bool showVersion = false;

            // Parse Command Line Options
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
                {
                    showHelp = true;
                }
                else if (arg.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-v", StringComparison.OrdinalIgnoreCase))
                {
                    showVersion = true;
                }
                else if (arg.Equals("--fix-only", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-f", StringComparison.OrdinalIgnoreCase))
                {
                    fixOnly = true;
                }
                else if (arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                {
                    _verbose = true;
                }
                else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                {
                    _silent = true;
                }
                else if (arg.Equals("--log", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    _logFilePath = args[++i];
                }
                else if (arg.Equals("--monitor", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
                {
                    monitorMode = true;
                    if (i + 1 < args.Length)
                    {
                        int interval;
                        if (int.TryParse(args[i + 1], out interval))
                        {
                            monitorIntervalMinutes = Math.Max(1, interval);
                            i++;
                        }
                    }
                }
                else
                {
                    forwardedArgs.Add(arg);
                }
            }

            // Always try attaching console if any args provided or requested
            if (showHelp || showVersion || _verbose || args.Length > 0)
            {
                TryAttachConsole();
            }

            if (showHelp)
            {
                PrintHelp();
                return 0;
            }

            if (showVersion)
            {
                LogInfo(string.Format("{0} v{1}", AppName, AppVersion));
                return 0;
            }

            try
            {
                if (monitorMode)
                {
                    LogInfo(string.Format("Starting background monitor mode. Scan interval: {0} min.", monitorIntervalMinutes));
                    RunMonitorLoop(monitorIntervalMinutes);
                    return 0;
                }

                // Standard execution: fix running or launch & fix
                int terminatedCount = ExecuteFixCycle(fixOnly, forwardedArgs.ToArray());
                LogInfo(string.Format("Execution complete. Total rogue threads terminated: {0}.", terminatedCount));
                return 0;
            }
            catch (Exception ex)
            {
                LogError(string.Format("Unhandled error: {0}", ex.Message));
                if (_verbose)
                {
                    LogError(ex.ToString());
                }
                return 1;
            }
        }

        private static int ExecuteFixCycle(bool fixOnly, string[] launchArgs)
        {
            Process[] existing = Process.GetProcessesByName("InDesign");
            bool justLaunched = false;

            if (existing == null || existing.Length == 0)
            {
                if (fixOnly)
                {
                    LogInfo("InDesign is not currently running. (--fix-only mode, skipping launch)");
                    return 0;
                }

                string indesignPath = FindInDesignExecutable();
                if (string.IsNullOrEmpty(indesignPath) || !File.Exists(indesignPath))
                {
                    LogError("Could not locate Adobe InDesign installation. Please specify path or launch InDesign manually.");
                    return 0;
                }

                LogInfo(string.Format("Launching InDesign: {0}", indesignPath));
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = indesignPath,
                    WorkingDirectory = Path.GetDirectoryName(indesignPath),
                    UseShellExecute = true
                };

                if (launchArgs != null && launchArgs.Length > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string a in launchArgs)
                    {
                        if (a.Contains(" "))
                        {
                            sb.Append(string.Format("\"{0}\" ", a));
                        }
                        else
                        {
                            sb.Append(string.Format("{0} ", a));
                        }
                    }
                    psi.Arguments = sb.ToString().TrimEnd();
                }

                Process.Start(psi);
                justLaunched = true;
            }

            // Find InDesign process
            Process indesign = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                Process[] procs = Process.GetProcessesByName("InDesign");
                if (procs != null && procs.Length > 0)
                {
                    indesign = procs[0];
                    break;
                }
                Thread.Sleep(1000);
            }

            if (indesign == null)
            {
                LogError("Failed to attach to InDesign process.");
                return 0;
            }

            LogInfo(string.Format("Attached to InDesign (PID: {0})", indesign.Id));

            // If just launched, wait for main window and plugin stabilization
            if (justLaunched)
            {
                LogInfo("Waiting for InDesign main window to load...");
                DateTime waitStart = DateTime.Now;
                while ((DateTime.Now - waitStart).TotalSeconds < 45)
                {
                    indesign.Refresh();
                    if (indesign.HasExited)
                    {
                        LogInfo("InDesign process exited prematurely.");
                        return 0;
                    }
                    if (indesign.MainWindowHandle != IntPtr.Zero)
                    {
                        break;
                    }
                    Thread.Sleep(1000);
                }

                LogInfo("InDesign window detected. Waiting 10s for CEP and font extensions to finish startup burst...");
                Thread.Sleep(10000);
            }

            return ScanAndTerminateRogueThreads(indesign);
        }

        private static int ScanAndTerminateRogueThreads(Process indesign)
        {
            if (indesign.HasExited) return 0;

            // 1. Identify Main UI thread to NEVER terminate it
            uint mainThreadId = 0;
            try
            {
                if (indesign.MainWindowHandle != IntPtr.Zero)
                {
                    uint pid;
                    mainThreadId = GetWindowThreadProcessId(indesign.MainWindowHandle, out pid);
                }
            }
            catch { }

            int primaryThreadId = -1;
            try
            {
                if (indesign.Threads.Count > 0)
                {
                    primaryThreadId = indesign.Threads[0].Id;
                }
            }
            catch { }

            LogInfo(string.Format("Protected Threads: Main UI Thread = {0}, Primary Thread = {1}", mainThreadId, primaryThreadId));

            // 2. Sample thread CPU consumption over 2 seconds
            Dictionary<int, TimeSpan> initialCpu = new Dictionary<int, TimeSpan>();
            foreach (ProcessThread t in indesign.Threads)
            {
                try
                {
                    initialCpu[t.Id] = t.TotalProcessorTime;
                }
                catch { }
            }

            LogInfo(string.Format("Sampling {0} threads for 2 seconds to detect infinite loops...", initialCpu.Count));
            Thread.Sleep(2000);

            indesign.Refresh();

            List<int> rogueThreads = new List<int>();
            foreach (ProcessThread t in indesign.Threads)
            {
                try
                {
                    // Never touch the Main UI thread or primary thread!
                    if (t.Id == (int)mainThreadId || t.Id == primaryThreadId)
                    {
                        continue;
                    }

                    TimeSpan oldTime;
                    if (initialCpu.TryGetValue(t.Id, out oldTime))
                    {
                        double cpuDeltaSec = (t.TotalProcessorTime - oldTime).TotalSeconds;
                        // A 100% busy-looping thread consumes >= 1.0s (up to 2.0s) out of a 2.0s sample window
                        if (cpuDeltaSec >= 1.0)
                        {
                            LogWarning(string.Format("Rogue thread detected! TID: {0}, CPU usage: {1:F2}s / 2.0s", t.Id, cpuDeltaSec));
                            rogueThreads.Add(t.Id);
                        }
                    }
                }
                catch { }
            }

            if (rogueThreads.Count == 0)
            {
                LogInfo("No rogue 100% CPU threads detected. All background threads are healthy.");
                return 0;
            }

            // 3. Terminate only the rogue background threads
            int terminatedCount = 0;
            foreach (int tid in rogueThreads)
            {
                try
                {
                    IntPtr hThread = OpenThread(THREAD_TERMINATE, false, (uint)tid);
                    if (hThread != IntPtr.Zero)
                    {
                        bool success = TerminateThread(hThread, 0);
                        CloseHandle(hThread);
                        if (success)
                        {
                            terminatedCount++;
                            LogSuccess(string.Format("Successfully terminated rogue thread (TID: {0}). CPU load normalized.", tid));
                        }
                        else
                        {
                            LogError(string.Format("Failed to terminate thread (TID: {0}). Error code: {1}", tid, Marshal.GetLastWin32Error()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError(string.Format("Error terminating thread {0}: {1}", tid, ex.Message));
                }
            }

            return terminatedCount;
        }

        private static void RunMonitorLoop(int intervalMinutes)
        {
            while (true)
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName("InDesign");
                    if (procs != null && procs.Length > 0)
                    {
                        foreach (Process p in procs)
                        {
                            ScanAndTerminateRogueThreads(p);
                        }
                    }
                    else
                    {
                        LogInfo("Monitor: InDesign is not running.");
                    }
                }
                catch (Exception ex)
                {
                    LogError(string.Format("Monitor error: {0}", ex.Message));
                }

                Thread.Sleep(intervalMinutes * 60 * 1000);
            }
        }

        public static string FindInDesignExecutable()
        {
            // 1. Check current directory
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InDesign.exe");
            if (File.Exists(localPath)) return localPath;

            // 2. Check Windows Registry App Paths
            string[] registryKeys = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\InDesign.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\InDesign.exe"
            };

            foreach (string keyPath in registryKeys)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        if (key != null)
                        {
                            string path = key.GetValue("") as string;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                return path;
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                    {
                        if (key != null)
                        {
                            string path = key.GetValue("") as string;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                return path;
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Known standard Adobe installation paths (from 2027 down to 2019)
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] versions = new string[] { "2027", "2026", "2025", "2024", "2023", "2022", "2021", "2020", "CC 2019" };

            foreach (string ver in versions)
            {
                string p = Path.Combine(programFiles, "Adobe", string.Format("Adobe InDesign {0}", ver), "InDesign.exe");
                if (File.Exists(p)) return p;
            }

            // 4. Dynamic search in Adobe folder
            try
            {
                string adobeDir = Path.Combine(programFiles, "Adobe");
                if (Directory.Exists(adobeDir))
                {
                    string[] dirs = Directory.GetDirectories(adobeDir, "Adobe InDesign*");
                    Array.Sort(dirs);
                    Array.Reverse(dirs); // Newest first
                    foreach (string dir in dirs)
                    {
                        string candidate = Path.Combine(dir, "InDesign.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { }

            return null;
        }

        #region Logging & Console Helpers

        private static void TryAttachConsole()
        {
            if (!_hasConsole)
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    _hasConsole = true;
                    try
                    {
                        IntPtr stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                        if (stdOut != IntPtr.Zero && stdOut != new IntPtr(-1))
                        {
                            SafeFileHandle safeHandle = new SafeFileHandle(stdOut, false);
                            FileStream fs = new FileStream(safeHandle, FileAccess.Write);
                            StreamWriter sw = new StreamWriter(fs, Encoding.Default) { AutoFlush = true };
                            Console.SetOut(sw);
                            Console.SetError(sw);
                        }
                    }
                    catch { }
                }
            }
        }

        private static void LogInfo(string message)
        {
            if (_silent) return;
            string formatted = string.Format("[{0:HH:mm:ss}] [INFO] {1}", DateTime.Now, message);
            WriteConsole(formatted, ConsoleColor.Gray);
            WriteLogFile(formatted);
        }

        private static void LogSuccess(string message)
        {
            if (_silent) return;
            string formatted = string.Format("[{0:HH:mm:ss}] [FIXED] {1}", DateTime.Now, message);
            WriteConsole(formatted, ConsoleColor.Green);
            WriteLogFile(formatted);
        }

        private static void LogWarning(string message)
        {
            if (_silent) return;
            string formatted = string.Format("[{0:HH:mm:ss}] [WARN] {1}", DateTime.Now, message);
            WriteConsole(formatted, ConsoleColor.Yellow);
            WriteLogFile(formatted);
        }

        private static void LogError(string message)
        {
            if (_silent) return;
            string formatted = string.Format("[{0:HH:mm:ss}] [ERROR] {1}", DateTime.Now, message);
            WriteConsole(formatted, ConsoleColor.Red);
            WriteLogFile(formatted);
        }

        private static void WriteConsole(string text, ConsoleColor color)
        {
            if (!_hasConsole && (_verbose || !_silent))
            {
                TryAttachConsole();
            }

            if (_hasConsole)
            {
                try
                {
                    ConsoleColor old = Console.ForegroundColor;
                    Console.ForegroundColor = color;
                    Console.WriteLine(text);
                    Console.ForegroundColor = old;
                }
                catch { }
            }
        }

        private static void WriteLogFile(string text)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;
            try
            {
                File.AppendAllText(_logFilePath, text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        private static void PrintHelp()
        {
            string help = string.Format(@"
======================================================================
  {0} v{1}
  Safe CPU 100% thread fix utility for Adobe InDesign (2020-2026+)
======================================================================

USAGE:
  ID_Thread_Fix.exe [OPTIONS] [FILES / ARGS...]

OPTIONS:
  -f, --fix-only          Scan and fix running InDesign without launching it.
  -m, --monitor [MIN]     Run continuously in background every [MIN] minutes (default: 5).
  -v, --verbose           Display detailed diagnostic output.
  -s, --silent            Run completely silently (no console output).
      --log <path>        Save log messages to the specified file.
  -h, --help              Show this help message.
      --version           Show version information.

EXAMPLES:
  # Launch InDesign as usual and auto-fix background runaway threads:
  ID_Thread_Fix.exe

  # Open a specific document while auto-fixing CPU:
  ID_Thread_Fix.exe ""C:\Projects\Brochure.indd""

  # Fix an already open, lagging InDesign instance immediately:
  ID_Thread_Fix.exe --fix-only

  # Run as a background monitor every 10 minutes:
  ID_Thread_Fix.exe --monitor 10 --log ""C:\Logs\indesign_fix.log""
", AppName, AppVersion);
            WriteConsole(help, ConsoleColor.Cyan);
        }

        #endregion
    }
}
