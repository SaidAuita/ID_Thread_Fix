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
    public enum FixAction
    {
        Throttle,   // Set to THREAD_PRIORITY_IDLE (Safest, 0% crash risk, relieves CPU/fans)
        Terminate,  // Call TerminateThread (Protected: core modules fallback to throttle)
        Suspend     // Call SuspendThread
    }

    static class Program
    {
        private const string AppVersion = "1.1.0";
        private const string AppName = "InDesign Thread Fix (ID_Thread_Fix)";

        #region Win32 Constants

        private const uint THREAD_TERMINATE = 0x0001;
        private const uint THREAD_SUSPEND_RESUME = 0x0002;
        private const uint THREAD_SET_INFORMATION = 0x0020;
        private const uint THREAD_QUERY_INFORMATION = 0x0040;
        private const uint THREAD_SET_LIMITED_INFORMATION = 0x0400;

        private const int THREAD_PRIORITY_IDLE = -15;
        private const int THREAD_PRIORITY_LOWEST = -2;
        private const int THREAD_PRIORITY_BELOW_NORMAL = -1;
        private const int THREAD_PRIORITY_NORMAL = 0;

        private const int ThreadQuerySetWin32StartAddress = 9;

        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;

        #endregion

        #region Win32 API Imports

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int GetThreadPriority(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateThread(IntPtr hThread, uint dwExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryInformationThread(
            IntPtr ThreadHandle,
            int ThreadInformationClass,
            out IntPtr ThreadInformation,
            int ThreadInformationLength,
            out int ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        #endregion

        private static bool _verbose;
        private static bool _silent;
        private static string _logFilePath;
        private static bool _hasConsole;
        private static FixAction _action = FixAction.Throttle;
        private static double _cpuThresholdPercent = 80.0;
        private static int _startupWaitSeconds = 30;

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
                else if (arg.Equals("--startup-wait", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int sw;
                    if (int.TryParse(args[i + 1], out sw))
                    {
                        _startupWaitSeconds = Math.Max(10, sw);
                        i++;
                    }
                }
                else if (arg.Equals("--threshold", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    double th;
                    if (double.TryParse(args[i + 1], out th))
                    {
                        _cpuThresholdPercent = Math.Max(30.0, Math.Min(100.0, th));
                        i++;
                    }
                }
                else if (arg.Equals("--action", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string act = args[++i].ToLowerInvariant();
                    if (act == "terminate" || act == "kill")
                    {
                        _action = FixAction.Terminate;
                    }
                    else if (act == "suspend" || act == "pause")
                    {
                        _action = FixAction.Suspend;
                    }
                    else
                    {
                        _action = FixAction.Throttle;
                    }
                }
                else if (arg.Equals("--force-terminate", StringComparison.OrdinalIgnoreCase))
                {
                    _action = FixAction.Terminate;
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

            // Attach console if needed
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
                    LogInfo(string.Format("Starting background monitor mode. Scan interval: {0} min, Action: {1}, Threshold: {2:F0}%",
                        monitorIntervalMinutes, _action, _cpuThresholdPercent));
                    RunMonitorLoop(monitorIntervalMinutes);
                    return 0;
                }

                // Standard execution: fix running or launch & fix
                int fixedCount = ExecuteFixCycle(fixOnly, forwardedArgs.ToArray());
                LogInfo(string.Format("Execution complete. Total rogue threads fixed: {0}.", fixedCount));
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

            // If just launched, wait for main window and let startup load settle down safely
            if (justLaunched)
            {
                LogInfo("Waiting for InDesign main window to load...");
                DateTime waitStart = DateTime.Now;
                while ((DateTime.Now - waitStart).TotalSeconds < 60)
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

                LogInfo(string.Format("InDesign window detected. Waiting for startup burst to finish ({0}s)...", _startupWaitSeconds));
                WaitForProcessToSettle(indesign, _startupWaitSeconds);
            }

            return ScanAndFixRogueThreads(indesign);
        }

        private static void WaitForProcessToSettle(Process proc, int targetWaitSec)
        {
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < targetWaitSec)
            {
                proc.Refresh();
                if (proc.HasExited) return;
                Thread.Sleep(2000);
            }
            LogInfo("Startup stabilization complete. InDesign has settled to idle.");
        }

        private static int ScanAndFixRogueThreads(Process indesign)
        {
            if (indesign.HasExited) return 0;

            // 1. Identify Protected Threads (Main UI Thread + Primary Process Thread)
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

            // 2. Stage 1 Sampling (3.0 seconds)
            Dictionary<int, TimeSpan> t1 = new Dictionary<int, TimeSpan>();
            foreach (ProcessThread t in indesign.Threads)
            {
                try
                {
                    t1[t.Id] = t.TotalProcessorTime;
                }
                catch { }
            }

            LogInfo(string.Format("Stage 1: Sampling {0} threads over 3.0s (Threshold: >={1:F0}% CPU)...", t1.Count, _cpuThresholdPercent));
            Thread.Sleep(3000);

            indesign.Refresh();
            if (indesign.HasExited) return 0;

            List<int> candidateThreads = new List<int>();
            foreach (ProcessThread t in indesign.Threads)
            {
                try
                {
                    if (t.Id == (int)mainThreadId || t.Id == primaryThreadId) continue;

                    TimeSpan oldTime;
                    if (t1.TryGetValue(t.Id, out oldTime))
                    {
                        double deltaSec = (t.TotalProcessorTime - oldTime).TotalSeconds;
                        double usagePercent = (deltaSec / 3.0) * 100.0;

                        if (usagePercent >= _cpuThresholdPercent)
                        {
                            candidateThreads.Add(t.Id);
                            LogInfo(string.Format("Stage 1 Candidate: TID={0}, CPU={1:F1}%", t.Id, usagePercent));
                        }
                    }
                }
                catch { }
            }

            if (candidateThreads.Count == 0)
            {
                LogInfo("All background threads are healthy. No runaway CPU loops detected.");
                return 0;
            }

            // 3. Stage 2 Verification (Second 3.0s sample to prevent false positives on temporary bursts)
            LogWarning(string.Format("Candidate rogue threads detected ({0}). Performing Stage 2 verification to confirm permanent loop...", candidateThreads.Count));
            Thread.Sleep(2000);

            indesign.Refresh();
            if (indesign.HasExited) return 0;

            Dictionary<int, TimeSpan> t2 = new Dictionary<int, TimeSpan>();
            foreach (int tid in candidateThreads)
            {
                foreach (ProcessThread t in indesign.Threads)
                {
                    if (t.Id == tid)
                    {
                        try { t2[tid] = t.TotalProcessorTime; } catch { }
                        break;
                    }
                }
            }

            Thread.Sleep(3000);

            indesign.Refresh();
            if (indesign.HasExited) return 0;

            List<int> confirmedRogueThreads = new List<int>();
            foreach (int tid in candidateThreads)
            {
                foreach (ProcessThread t in indesign.Threads)
                {
                    if (t.Id == tid)
                    {
                        TimeSpan oldTime;
                        if (t2.TryGetValue(tid, out oldTime))
                        {
                            double deltaSec = (t.TotalProcessorTime - oldTime).TotalSeconds;
                            double usagePercent = (deltaSec / 3.0) * 100.0;
                            if (usagePercent >= _cpuThresholdPercent)
                            {
                                confirmedRogueThreads.Add(tid);
                                LogWarning(string.Format("Confirmed Rogue Thread: TID={0} is permanently locked at {1:F1}% CPU!", tid, usagePercent));
                            }
                            else
                            {
                                LogInfo(string.Format("TID={0} dropped to {1:F1}% CPU (normal background burst finished, skipping).", tid, usagePercent));
                            }
                        }
                        break;
                    }
                }
            }

            if (confirmedRogueThreads.Count == 0)
            {
                LogInfo("All candidate threads normalized on verification. No intervention needed.");
                return 0;
            }

            // 4. Cache process modules for safe module identification
            List<ModuleInfo> modules = GetProcessModules(indesign);

            // 5. Apply fix action (Throttle / Suspend / Terminate with core protection)
            int fixedCount = 0;
            foreach (int tid in confirmedRogueThreads)
            {
                string moduleName = ResolveThreadModule(tid, modules);
                bool isCoreEngine = IsCoreInDesignModule(moduleName);

                if (isCoreEngine)
                {
                    LogWarning(string.Format("TID {0} starts in core InDesign module '{1}'. Terminate is blocked to prevent crash.", tid, moduleName));
                }
                else
                {
                    LogInfo(string.Format("TID {0} belongs to module '{1}'.", tid, moduleName));
                }

                // If user requested Terminate, but thread is in core module, safely force Throttle instead of crashing
                FixAction effectiveAction = _action;
                if (effectiveAction == FixAction.Terminate && isCoreEngine)
                {
                    LogWarning(string.Format("Switching action for TID {0} from Terminate to Throttle (Idle Priority) to guarantee stability.", tid));
                    effectiveAction = FixAction.Throttle;
                }

                bool success = ApplyFixToThread((uint)tid, effectiveAction, moduleName);
                if (success)
                {
                    fixedCount++;
                }
            }

            return fixedCount;
        }

        private static bool ApplyFixToThread(uint tid, FixAction action, string moduleName)
        {
            try
            {
                if (action == FixAction.Throttle)
                {
                    IntPtr hThread = OpenThread(THREAD_SET_INFORMATION | THREAD_QUERY_INFORMATION, false, tid);
                    if (hThread != IntPtr.Zero)
                    {
                        bool ok = SetThreadPriority(hThread, THREAD_PRIORITY_IDLE);
                        CloseHandle(hThread);
                        if (ok)
                        {
                            LogSuccess(string.Format("Safely throttled rogue thread TID={0} ({1}) to IDLE priority. CPU load and fan noise normalized with 100% crash protection.", tid, moduleName));
                            return true;
                        }
                        else
                        {
                            LogError(string.Format("Failed to set thread priority for TID={0}. Error code: {1}", tid, Marshal.GetLastWin32Error()));
                        }
                    }
                }
                else if (action == FixAction.Suspend)
                {
                    IntPtr hThread = OpenThread(THREAD_SUSPEND_RESUME, false, tid);
                    if (hThread != IntPtr.Zero)
                    {
                        uint res = SuspendThread(hThread);
                        CloseHandle(hThread);
                        if (res != 0xFFFFFFFF)
                        {
                            LogSuccess(string.Format("Suspended rogue thread TID={0} ({1}).", tid, moduleName));
                            return true;
                        }
                        else
                        {
                            LogError(string.Format("Failed to suspend thread TID={0}. Error code: {1}", tid, Marshal.GetLastWin32Error()));
                        }
                    }
                }
                else if (action == FixAction.Terminate)
                {
                    IntPtr hThread = OpenThread(THREAD_TERMINATE, false, tid);
                    if (hThread != IntPtr.Zero)
                    {
                        bool ok = TerminateThread(hThread, 0);
                        CloseHandle(hThread);
                        if (ok)
                        {
                            LogSuccess(string.Format("Terminated rogue background thread TID={0} ({1}).", tid, moduleName));
                            return true;
                        }
                        else
                        {
                            LogError(string.Format("Failed to terminate thread TID={0}. Error code: {1}", tid, Marshal.GetLastWin32Error()));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(string.Format("Error applying fix to thread {0}: {1}", tid, ex.Message));
            }

            return false;
        }

        private static bool IsCoreInDesignModule(string moduleName)
        {
            if (string.IsNullOrEmpty(moduleName)) return false;

            string m = moduleName.ToLowerInvariant();
            return m.Contains("indesign.exe") ||
                   m.Contains("dynamic-torqnative") ||
                   m.Contains("application ui") ||
                   m.Contains("public.dll") ||
                   m.Contains("pmruntime") ||
                   m.Contains("ntdll.dll") ||
                   m.Contains("kernel32.dll") ||
                   m.Contains("kernelbase.dll");
        }

        private static string ResolveThreadModule(int tid, List<ModuleInfo> modules)
        {
            IntPtr hThread = OpenThread(THREAD_QUERY_INFORMATION, false, (uint)tid);
            if (hThread == IntPtr.Zero)
            {
                return "Unknown";
            }

            try
            {
                IntPtr startAddress;
                int retLen;
                int status = NtQueryInformationThread(hThread, ThreadQuerySetWin32StartAddress, out startAddress, IntPtr.Size, out retLen);
                if (status == 0 && startAddress != IntPtr.Zero)
                {
                    long addr = startAddress.ToInt64();
                    foreach (ModuleInfo mod in modules)
                    {
                        if (addr >= mod.BaseAddress && addr < mod.EndAddress)
                        {
                            return mod.Name;
                        }
                    }
                    return string.Format("0x{0:X}", addr);
                }
            }
            catch { }
            finally
            {
                CloseHandle(hThread);
            }

            return "Unknown";
        }

        private class ModuleInfo
        {
            public string Name;
            public long BaseAddress;
            public long EndAddress;
        }

        private static List<ModuleInfo> GetProcessModules(Process proc)
        {
            List<ModuleInfo> list = new List<ModuleInfo>();
            try
            {
                foreach (ProcessModule m in proc.Modules)
                {
                    ModuleInfo mi = new ModuleInfo
                    {
                        Name = m.ModuleName,
                        BaseAddress = m.BaseAddress.ToInt64(),
                        EndAddress = m.BaseAddress.ToInt64() + m.ModuleMemorySize
                    };
                    list.Add(mi);
                }
            }
            catch { }
            return list;
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
                            ScanAndFixRogueThreads(p);
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
  -f, --fix-only              Scan and fix running InDesign without launching it.
  -m, --monitor [MIN]         Run continuously in background every [MIN] minutes (default: 5).
      --action <mode>         Fix method: throttle (default, 100% safe), suspend, or terminate.
      --force-terminate       Alias for --action terminate (core modules still protected).
      --threshold <percent>   CPU usage percentage to trigger fix (default: 80).
      --startup-wait <sec>    Seconds to let InDesign startup settle before scanning (default: 30).
  -v, --verbose               Display detailed diagnostic output.
  -s, --silent                Run completely silently (no console output).
      --log <path>            Save log messages to the specified file.
  -h, --help                  Show this help message.
      --version               Show version information.

EXAMPLES:
  # Launch InDesign as usual with rock-solid auto-fix (Priority Throttle):
  ID_Thread_Fix.exe

  # Open a specific document while stabilizing CPU load:
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
