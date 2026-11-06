// NEONDECK DESKTOP GADGETS | Portable Windows 11 dashboard
// Uses Windows/.NET Framework APIs and optional NVIDIA / LibreHardwareMonitor sensors.
// Build: NeonDeck.exe portable GUI launcher (no installer / command window).
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Microsoft.Win32;

namespace NeonDeck
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (Mutex single = new Mutex(true, @"Local\NeonDeckDesktopGadgets", out first))
            {
                if (!first)
                {
                    MessageBox.Show("NeonDeck is already running. Find its icon in the system tray.",
                        "NeonDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new DeckContext());
            }
        }
    }

    internal sealed class ProcUsage
    {
        public string Name = "";
        public double Load;
    }
    internal sealed class DiskState
    {
        public int Index;
        public string Name = "Disk";
        public string Volumes = "";
        public string VolumeSummary = "";
        public string VolumeLabels = ""; // e.g. J: SSD Samsung 512 GB (user-assigned label)
        // Usage covers mounted volumes only. Unallocated / hidden partitions are NOT counted.
        public double MountedTotalGB;
        public double MountedUsedGB;
        public double? MountedFillPct;
        public double SizeGB;
        public double ReadMB;
        public double WriteMB;
        public bool IoAvailable; // do not present an unavailable performance counter as zero
        public string Health = "N/A";
        public double? Temp;
        public double? Wear;
        public string Bus = "";
        // Mapped SMB/network shares are logical volumes, not physical devices.
        public bool IsNetwork;
        public bool Connected;
        public string RemotePath = "";
        public string DriveLetter = "";
    }
    internal sealed class DeckData
    {
        public string CpuName = "Detecting CPU...";
        public double CpuLoad;
        public double CpuClockMHz;
        public double? CpuTemp;
        public double[] Threads = new double[0];
        public ProcUsage[] Top = new ProcUsage[0];
        public double RamUsed;
        public double RamTotal;
        public float[] CpuHistory = new float[0];

        public string GpuName = "Detecting GPU...";
        public bool GpuNvidia;
        public double? GpuLoad;
        public double? GpuTemp;
        public double? VramUsed;
        public double? VramTotal;
        public double? GpuPower;
        public double? GpuPowerLimit;
        public double? GpuFan;
        public double? GpuClock;
        public double? GpuMemClock;
        public double? GpuEncoder;
        public string GpuPcie = "N/A";
        public string GpuDriver = "";
        public float[] GpuHistory = new float[0];

        public string NicName = "Detecting network...";
        public string NicIp = "N/A";
        public string NicLink = "N/A";
        public string WifiSignal = "N/A";
        public double DownMbps;
        public double UpMbps;
        public double TotalDownGB;
        public double TotalUpGB;
        public int? PingMs;
        public float[] DownHistory = new float[0];
        public float[] UpHistory = new float[0];
        public DiskState[] Disks = new DiskState[0];
        public DateTime Collected = DateTime.Now;
    }
    internal static class Helpers
    {
        public static double Clamp(double v, double min, double max) { return Math.Max(min, Math.Min(max, v)); }
        public static double Number(object val, double fallback)
        {
            if (val == null) return fallback;
            double x;
            if (double.TryParse(Convert.ToString(val), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)) return x;
            return fallback;
        }
        public static double? NullableNumber(object val)
        {
            if (val == null) return null;
            string s = Convert.ToString(val).Trim();
            if (s.Length == 0 || s == "N/A" || s == "[Not Supported]" || s == "Not Supported") return null;
            double v = Number(s.Replace(" %", ""), double.NaN);
            return double.IsNaN(v) ? (double?)null : v;
        }
        public static string Show(double? v, string unit, string format)
        {
            return v.HasValue ? v.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + unit : "N/A";
        }
        public static string Run(string exe, string args, int timeout)
        {
            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = exe;
                    p.StartInfo.Arguments = args;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = false;
                    p.Start();
                    if (!p.WaitForExit(timeout))
                    {
                        try { p.Kill(); } catch { }
                        return "";
                    }
                    return p.StandardOutput.ReadToEnd();
                }
            }
            catch { return ""; }
        }
        public static string WmiStr(ManagementBaseObject o, string key, string fallback)
        {
            try { object v = o[key]; return v == null ? fallback : Convert.ToString(v); }
            catch { return fallback; }
        }
        public static object WmiValue(ManagementBaseObject o, string key)
        {
            try { return o[key]; } catch { return null; }
        }
        public static ManagementObjectCollection Query(string scope, string sql)
        {
            using (ManagementObjectSearcher ms = new ManagementObjectSearcher(scope, sql)) return ms.Get();
        }
        public static void AddPoint(Queue<float> q, float v)
        {
            q.Enqueue(v);
            while (q.Count > 64) q.Dequeue();
        }
    }
    internal static class DeckCleanup
    {
        // Native Windows shell API: true single-click, silent Recycle Bin cleanup.
        // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND.
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);

        public static string EmptyBin()
        {
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x00000001u | 0x00000002u | 0x00000004u);
            if (hr == 0) return "RECYCLE BIN EMPTIED - ALL VOLUMES";
            return "RECYCLE BIN: WINDOWS ERROR 0x" + unchecked((uint)hr).ToString("X8");
        }

        private sealed class Counts
        {
            public long Bytes;
            public int Deleted;
            public int Skipped;
            public int Visited;
        }

        private static void CleanChildren(string path, DateTime cutoffUtc, Counts counts)
        {
            // Do not follow directory junctions/reparse points, even if found in Temp.
            FileSystemInfo[] entries;
            try { entries = new DirectoryInfo(path).GetFileSystemInfos(); }
            catch { counts.Skipped++; return; }
            foreach (FileSystemInfo entry in entries)
            {
                // Cap the number of visited objects to keep cleanup bounded.
                if (++counts.Visited > 100000) { counts.Skipped++; return; }
                try
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { counts.Skipped++; continue; }
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        CleanChildren(entry.FullName, cutoffUtc, counts);
                        try
                        {
                            // Never use recursive Delete; only remove genuinely empty old dirs.
                            if (entry.LastWriteTimeUtc < cutoffUtc && entry.CreationTimeUtc < cutoffUtc)
                                Directory.Delete(entry.FullName, false);
                        }
                        catch { }
                    }
                    else
                    {
                        // Exclude recently created/updated files; don't force-delete locked files.
                        if (entry.LastWriteTimeUtc >= cutoffUtc || entry.CreationTimeUtc >= cutoffUtc)
                        { counts.Skipped++; continue; }
                        FileInfo file = new FileInfo(entry.FullName);
                        long bytes = file.Length;
                        file.Delete();
                        counts.Bytes += bytes;
                        counts.Deleted++;
                    }
                }
                catch { counts.Skipped++; }
            }
        }

        public static string CleanSafeTemp()
        {
            // Narrow, explicit scope. No Downloads, Windows Update, WinSxS,
            // cache of browsers, Prefetch, restore points, or user documents.
            string userTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
            string windowsTemp = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")).TrimEnd('\\');
            Counts c = new Counts();
            DateTime now = DateTime.UtcNow;
            if (Directory.Exists(userTemp)) CleanChildren(userTemp, now.AddDays(-7), c);
            if (!userTemp.Equals(windowsTemp, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(windowsTemp)) CleanChildren(windowsTemp, now.AddDays(-14), c);
            return "TEMP: " + c.Deleted + " FILES, " +
                (c.Bytes / (1024.0 * 1024.0)).ToString("0.0") + " MiB FREED" +
                (c.Skipped > 0 ? ", " + c.Skipped + " SKIPPED" : "");
        }
    }

    internal sealed class Probe
    {
        private readonly Queue<float> cpuQ = new Queue<float>();
        private readonly Queue<float> gpuQ = new Queue<float>();
        private readonly Queue<float> downQ = new Queue<float>();
        private readonly Queue<float> upQ = new Queue<float>();
        private readonly Dictionary<int, TimeSpan> prevProc = new Dictionary<int, TimeSpan>();
        private readonly Dictionary<string, long[]> lastNic = new Dictionary<string, long[]>();
        private string cpuName = "CPU";
        private string gpuName = "GPU";
        private double cpuClock;
        private DateTime procStamp = DateTime.MinValue;
        private DateTime nicStamp = DateTime.MinValue;
        private DateTime slowStamp = DateTime.MinValue;
        private DateTime smartStamp = DateTime.MinValue;
        private DateTime diskStamp = DateTime.MinValue;
        private List<DiskState> diskInventory = new List<DiskState>();
        private readonly Dictionary<string, string[]> smartInfo = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> storageTemperatures = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        // Physical disk raw counters are cumulative; calculate bytes/s from two samples.
        // Prefer them to the formatted WMI counters, which can return stale 0 readings.
        private sealed class DiskIoSample
        {
            public ulong Read, Write, Tick, Freq;
        }
        private readonly Dictionary<int, DiskIoSample> previousDiskIo = new Dictionary<int, DiskIoSample>();
        private double trafficDown;
        private double trafficUp;
        private string nvidiaSmi;
        private bool checkedNvidia;

        private static string Normalize(string s)
        {
            return Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]+", "");
        }
        public DeckData Poll()
        {
            DeckData d = new DeckData();
            try { ReadCpu(d); } catch { }
            try { ReadMemory(d); } catch { }
            try { ReadTopProcesses(d); } catch { }
            try { ReadGpu(d); } catch { }
            try { ReadLibreSensors(d); } catch { }
            try { ReadNetwork(d); } catch { }
            try { ReadDisk(d); } catch { }
            Helpers.AddPoint(cpuQ, (float)d.CpuLoad);
            Helpers.AddPoint(gpuQ, (float)(d.GpuLoad ?? 0));
            Helpers.AddPoint(downQ, (float)d.DownMbps);
            Helpers.AddPoint(upQ, (float)d.UpMbps);
            d.CpuHistory = cpuQ.ToArray();
            d.GpuHistory = gpuQ.ToArray();
            d.DownHistory = downQ.ToArray();
            d.UpHistory = upQ.ToArray();
            d.Collected = DateTime.Now;
            return d;
        }
        private void ReadCpu(DeckData d)
        {
            List<KeyValuePair<int, double>> cores = new List<KeyValuePair<int, double>>();
            using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", "SELECT Name,PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor"))
            {
                foreach (ManagementObject row in rows)
                {
                    using (row)
                    {
                        string name = Helpers.WmiStr(row, "Name", "");
                        double load = Helpers.Clamp(Helpers.Number(Helpers.WmiValue(row, "PercentProcessorTime"), 0), 0, 100);
                        int num;
                        if (name.Equals("_Total", StringComparison.OrdinalIgnoreCase)) d.CpuLoad = load;
                        else if (int.TryParse(name, out num)) cores.Add(new KeyValuePair<int, double>(num, load));
                    }
                }
            }
            d.Threads = cores.OrderBy(c => c.Key).Select(c => c.Value).ToArray();
            if (d.Threads.Length == 0) d.Threads = new double[Environment.ProcessorCount];
            if (d.CpuLoad == 0 && cores.Count > 0) d.CpuLoad = cores.Average(c => c.Value);
            if (slowStamp == DateTime.MinValue || (DateTime.Now - slowStamp).TotalSeconds > 12)
            {
                try
                {
                    using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", "SELECT Name,CurrentClockSpeed FROM Win32_Processor"))
                    {
                        foreach (ManagementObject cpu in rows)
                        {
                            using (cpu)
                            {
                                cpuName = Helpers.WmiStr(cpu, "Name", "CPU").Trim();
                                cpuClock = Helpers.Number(Helpers.WmiValue(cpu, "CurrentClockSpeed"), 0);
                                break;
                            }
                        }
                    }
                }
                catch { }
                slowStamp = DateTime.Now;
            }
            d.CpuName = cpuName;
            d.CpuClockMHz = cpuClock;
        }
        private void ReadMemory(DeckData d)
        {
            using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", "SELECT FreePhysicalMemory,TotalVisibleMemorySize FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject os in rows)
                {
                    using (os)
                    {
                        double total = Helpers.Number(Helpers.WmiValue(os, "TotalVisibleMemorySize"), 0) / (1024 * 1024);
                        double free = Helpers.Number(Helpers.WmiValue(os, "FreePhysicalMemory"), 0) / (1024 * 1024);
                        d.RamTotal = total;
                        d.RamUsed = Math.Max(0, total - free);
                        break;
                    }
                }
            }
        }
        private void ReadTopProcesses(DeckData d)
        {
            Dictionary<int, TimeSpan> now = new Dictionary<int, TimeSpan>();
            List<ProcUsage> list = new List<ProcUsage>();
            DateTime t = DateTime.UtcNow;
            double ms = (t - procStamp).TotalMilliseconds;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    int id = p.Id;
                    TimeSpan cpu = p.TotalProcessorTime;
                    now[id] = cpu;
                    if (prevProc.ContainsKey(id) && ms > 0)
                    {
                        double pct = Helpers.Clamp((cpu - prevProc[id]).TotalMilliseconds / ms * 100 / Environment.ProcessorCount, 0, 100);
                        if (pct > 0.09) list.Add(new ProcUsage { Name = p.ProcessName + ".exe", Load = pct });
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            prevProc.Clear();
            foreach (KeyValuePair<int, TimeSpan> e in now) prevProc[e.Key] = e.Value;
            procStamp = t;
            d.Top = list.OrderByDescending(p => p.Load).Take(5).ToArray();
        }
        private void DiscoverNvidia()
        {
            if (checkedNvidia) return;
            checkedNvidia = true;
            List<string> names = new List<string>();
            names.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\nvidia-smi.exe"));
            names.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe"));
            names.Add("nvidia-smi.exe");
            foreach (string s in names)
            {
                if (s != "nvidia-smi.exe" && !File.Exists(s)) continue;
                string output = Helpers.Run(s, "--query-gpu=name --format=csv,noheader", 2000);
                if (output.Trim().Length > 1) { nvidiaSmi = s; break; }
            }
        }
        private void ReadGpu(DeckData d)
        {
            DiscoverNvidia();
            if (!String.IsNullOrEmpty(nvidiaSmi))
            {
                string keys = "name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,power.limit,fan.speed,clocks.current.graphics,clocks.current.memory,utilization.encoder,pcie.link.gen.current,pcie.link.width.current,driver_version";
                string result = Helpers.Run(nvidiaSmi, "--query-gpu=" + keys + " --format=csv,noheader,nounits", 3000);
                string line = result.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!String.IsNullOrEmpty(line))
                {
                    string[] p = line.Split(',').Select(t => t.Trim()).ToArray();
                    if (p.Length >= 14)
                    {
                        d.GpuNvidia = true;
                        d.GpuName = p[0];
                        d.GpuLoad = Helpers.NullableNumber(p[1]);
                        d.GpuTemp = Helpers.NullableNumber(p[2]);
                        d.VramUsed = Helpers.NullableNumber(p[3]);
                        d.VramTotal = Helpers.NullableNumber(p[4]);
                        d.GpuPower = Helpers.NullableNumber(p[5]);
                        d.GpuPowerLimit = Helpers.NullableNumber(p[6]);
                        d.GpuFan = Helpers.NullableNumber(p[7]);
                        d.GpuClock = Helpers.NullableNumber(p[8]);
                        d.GpuMemClock = Helpers.NullableNumber(p[9]);
                        d.GpuEncoder = Helpers.NullableNumber(p[10]);
                        d.GpuPcie = (p[11] == "N/A" || p[12] == "N/A") ? "N/A" : "Gen " + p[11] + "  x" + p[12];
                        d.GpuDriver = p[13];
                        return;
                    }
                }
            }
            if (gpuName == "GPU")
            {
                try
                {
                    using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", "SELECT Name FROM Win32_VideoController"))
                    {
                        foreach (ManagementObject v in rows)
                        {
                            using (v) { gpuName = Helpers.WmiStr(v, "Name", "GPU"); break; }
                        }
                    }
                }
                catch { }
            }
            d.GpuName = gpuName;
            // Windows GPU Engine formatted counters, best-effort fallback for non-NVIDIA GPUs.
            try
            {
                double max = 0;
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", "SELECT Name,UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"))
                {
                    foreach (ManagementObject v in rows)
                    {
                        using (v)
                        {
                            string n = Helpers.WmiStr(v, "Name", "");
                            if (n.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("engtype_Compute", StringComparison.OrdinalIgnoreCase) >= 0)
                                max = Math.Max(max, Helpers.Number(Helpers.WmiValue(v, "UtilizationPercentage"), 0));
                        }
                    }
                }
                d.GpuLoad = Helpers.Clamp(max, 0, 100);
            }
            catch { }
        }
        private void ReadLibreSensors(DeckData d)
        {
            storageTemperatures.Clear();
            // Optional LOCAL WMI bridge provided by the separately running portable
            // LibreHardwareMonitor (or OpenHardwareMonitor). No external DLLs required.
            string[] providers = new string[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" };
            foreach (string ns in providers)
            {
                try
                {
                    Dictionary<string, string> diskNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        using (ManagementObjectCollection hs = Helpers.Query(ns,
                            "SELECT Identifier,Name,HardwareType FROM Hardware"))
                        {
                            foreach (ManagementObject hw in hs)
                            {
                                using (hw)
                                {
                                    string kind = Helpers.WmiStr(hw, "HardwareType", "");
                                    string id = Helpers.WmiStr(hw, "Identifier", "");
                                    if ((kind.Equals("Storage", StringComparison.OrdinalIgnoreCase) ||
                                         kind.Equals("HDD", StringComparison.OrdinalIgnoreCase)) && id.Length > 0)
                                        diskNames[id] = Helpers.WmiStr(hw, "Name", "");
                                }
                            }
                        }
                    }
                    catch { /* The Hardware class is optional. */ }
                    int bestCpuScore = 0;
                    using (ManagementObjectCollection rows = Helpers.Query(ns,
                        "SELECT Name,SensorType,Value,Parent,Identifier FROM Sensor"))
                    {
                        foreach (ManagementObject sensor in rows)
                        {
                            using (sensor)
                            {
                                string name = Helpers.WmiStr(sensor, "Name", "");
                                string type = Helpers.WmiStr(sensor, "SensorType", "");
                                string parent = Helpers.WmiStr(sensor, "Parent", "");
                                string ident = Helpers.WmiStr(sensor, "Identifier", "");
                                double? value = Helpers.NullableNumber(Helpers.WmiValue(sensor, "Value"));
                                if (!value.HasValue) continue;
                                bool cpuParent = parent.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 ident.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0;
                                bool gpuParent = parent.IndexOf("gpu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 ident.IndexOf("gpu", StringComparison.OrdinalIgnoreCase) >= 0;
                                if (type.Equals("Temperature", StringComparison.OrdinalIgnoreCase) &&
                                    value.Value > 0 && value.Value < 125)
                                {
                                    int rank = 0;
                                    if (cpuParent)
                                    {
                                        if (name.IndexOf("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            name.IndexOf("CPU Package", StringComparison.OrdinalIgnoreCase) >= 0) rank = 5;
                                        else if (name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0) rank = 4;
                                        else if (name.IndexOf("CPU Core", StringComparison.OrdinalIgnoreCase) >= 0) rank = 3;
                                        else if (name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0) rank = 2;
                                    }
                                    if (rank > bestCpuScore) { bestCpuScore = rank; d.CpuTemp = value.Value; }
                                    if (gpuParent && !d.GpuTemp.HasValue &&
                                        name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                                        d.GpuTemp = value.Value;
                                    string diskName;
                                    if (String.IsNullOrEmpty(parent) || !diskNames.ContainsKey(parent))
                                    {
                                        // Some sensor bridges omit Parent; the identifier
                                        // contains the hardware identifier as its prefix.
                                        string matchId = diskNames.Keys.FirstOrDefault(id =>
                                            ident.StartsWith(id.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
                                        if (!String.IsNullOrEmpty(matchId)) parent = matchId;
                                    }
                                    if (diskNames.TryGetValue(parent, out diskName) && !String.IsNullOrEmpty(diskName))
                                        storageTemperatures[diskName] = value.Value;
                                }
                                if (!d.GpuNvidia && gpuParent &&
                                    name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (type.Equals("Load", StringComparison.OrdinalIgnoreCase)) d.GpuLoad = value.Value;
                                    if (type.Equals("Clock", StringComparison.OrdinalIgnoreCase)) d.GpuClock = value.Value;
                                }
                            }
                        }
                    }
                    break; // One working provider is enough.
                }
                catch { /* A missing/inactive provider must not break portable mode. */ }
            }
        }
        private void ReadNetwork(DeckData d)
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            var candidates = nics.Where(n => n.OperationalStatus == OperationalStatus.Up &&
                (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                n.GetIPProperties().GatewayAddresses.Count > 0).ToArray();
            if (candidates.Length == 0) candidates = nics.Where(n => n.OperationalStatus == OperationalStatus.Up &&
                (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)).ToArray();
            if (candidates.Length == 0) return;
            // Prefer the active default gateway and the fastest connected adapter.
            NetworkInterface nic = candidates.OrderByDescending(n => n.Speed).First();
            d.NicName = nic.Name;
            d.NicLink = nic.Speed > 0 ? (nic.Speed / 1000000.0).ToString("0") + " Mbit/s" : "N/A";
            try
            {
                foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { d.NicIp = ip.Address.ToString(); break; }
            }
            catch { }
            DateTime stamp = DateTime.UtcNow;
            double elapsed = (stamp - nicStamp).TotalSeconds;
            nicStamp = stamp;
            IPv4InterfaceStatistics stat = nic.GetIPv4Statistics();
            long downBytes = stat.BytesReceived;
            long upBytes = stat.BytesSent;
            long[] prev;
            if (elapsed > .05 && lastNic.TryGetValue(nic.Id, out prev))
            {
                long rx = Math.Max(0, downBytes - prev[0]);
                long tx = Math.Max(0, upBytes - prev[1]);
                d.DownMbps = rx * 8.0 / elapsed / 1000000.0;
                d.UpMbps = tx * 8.0 / elapsed / 1000000.0;
                trafficDown += rx;
                trafficUp += tx;
            }
            lastNic[nic.Id] = new long[] { downBytes, upBytes };
            d.TotalDownGB = trafficDown / 1000000000.0;
            d.TotalUpGB = trafficUp / 1000000000.0;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            {
                string wlan = Helpers.Run("netsh.exe", "wlan show interfaces", 1800);
                Match m = Regex.Match(wlan, @"(?im)^\s*(?:Signal|Sygnał)\s*:\s*(\d+)\s*%");
                if (m.Success) d.WifiSignal = m.Groups[1].Value + "%";
            }
            else d.WifiSignal = "Ethernet";
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply rep = ping.Send("1.1.1.1", 400);
                    if (rep != null && rep.Status == IPStatus.Success) d.PingMs = (int)rep.RoundtripTime;
                }
            }
            catch { }
        }
        public void ForceDiskInventoryRefresh() { diskStamp = DateTime.MinValue; }

        // Use three independent discovery paths. WMI provides capacity when the share is
        // connected; DriveInfo picks up live non-persistent mappings; HKCU\Network keeps
        // offline persistent mappings visible instead of silently dropping them.
        private static DiskState EnsureNetwork(Dictionary<string, DiskState> found, string letter)
        {
            letter = (letter ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(letter, @"^[A-Z]:$")) return null;
            DiskState item;
            if (!found.TryGetValue(letter, out item))
            {
                item = new DiskState { IsNetwork = true, Connected = false,
                    DriveLetter = letter, Volumes = letter, Index = -1 - (letter[0] - 'A'),
                    Name = letter + "  NETWORK SHARE", VolumeSummary = "SPACE N/A" };
                found[letter] = item;
            }
            return item;
        }
        private static void MergeNetworkWmi(Dictionary<string, DiskState> mapped, string query)
        {
            try
            {
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", query))
                {
                    foreach (ManagementObject o in rows)
                    {
                        using (o)
                        {
                            DiskState item = EnsureNetwork(mapped, Helpers.WmiStr(o, "DeviceID", ""));
                            if (item == null) continue;
                            string remote = Helpers.WmiStr(o, "ProviderName", "");
                            if (!String.IsNullOrWhiteSpace(remote)) item.RemotePath = remote;
                            double total = Helpers.Number(Helpers.WmiValue(o, "Size"), 0);
                            double free = Helpers.Number(Helpers.WmiValue(o, "FreeSpace"), -1);
                            if (total > 0 && free >= 0)
                            {
                                item.SizeGB = total / 1000000000.0;
                                item.MountedTotalGB = item.SizeGB;
                                item.MountedUsedGB = Math.Max(0, total - free) / 1000000000.0;
                                item.MountedFillPct = Helpers.Clamp(100 * (total - free) / total, 0, 100);
                                item.Connected = true;
                            }
                        }
                    }
                }
            }
            catch { /* Some systems restrict Win32_MappedLogicalDisk; other methods remain. */ }
        }
        // Win32_LogicalDisk.VolumeName is the label manually entered in Windows Explorer.
        // This is NOT the physical disk's manufacturer/model.
        private static Dictionary<string, string> ReadVolumeLabels()
        {
            Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2",
                    "SELECT DeviceID,VolumeName FROM Win32_LogicalDisk"))
                {
                    foreach (ManagementObject o in rows)
                    {
                        using (o)
                        {
                            string letter = Helpers.WmiStr(o, "DeviceID", "").Trim().ToUpperInvariant();
                            if (Regex.IsMatch(letter, @"^[A-Z]:$"))
                                labels[letter] = Helpers.WmiStr(o, "VolumeName", "").Trim();
                        }
                    }
                }
            }
            catch { /* Label is optional and may not be exposed on offline mappings. */ }
            return labels;
        }
        private static string DescribeVolume(string letter, Dictionary<string, string> names)
        {
            string label;
            if (!names.TryGetValue(letter, out label) || String.IsNullOrWhiteSpace(label))
                return letter + " (no label)";
            return letter + " " + label.Trim();
        }
        private static List<DiskState> ReadNetworkInventory(Dictionary<string, string> volumeNames)
        {
            Dictionary<string, DiskState> mapped = new Dictionary<string, DiskState>(StringComparer.OrdinalIgnoreCase);
            // Windows maps these drives per user. Query in the *same non-elevated user
            // context* as Explorer or they may not be visible due to UAC token separation.
            MergeNetworkWmi(mapped,
                "SELECT DeviceID,ProviderName,Size,FreeSpace FROM Win32_LogicalDisk WHERE DriveType=4");
            MergeNetworkWmi(mapped,
                "SELECT DeviceID,ProviderName,Size,FreeSpace FROM Win32_MappedLogicalDisk");
            try
            {
                foreach (DriveInfo volume in DriveInfo.GetDrives())
                {
                    if (volume.DriveType != DriveType.Network) continue;
                    EnsureNetwork(mapped, volume.Name.TrimEnd('\\'));
                    // Avoid IsReady/TotalSize on offline shares: these calls can block.
                }
            }
            catch { }
            try
            {
                using (RegistryKey baseKey = Registry.CurrentUser.OpenSubKey(@"Network", false))
                {
                    if (baseKey != null)
                    {
                        foreach (string letter in baseKey.GetSubKeyNames())
                        {
                            DiskState item = EnsureNetwork(mapped, letter + ":");
                            if (item == null) continue;
                            using (RegistryKey key = baseKey.OpenSubKey(letter, false))
                            {
                                if (key == null) continue;
                                string remote = Convert.ToString(key.GetValue("RemotePath", ""));
                                if (String.IsNullOrEmpty(item.RemotePath)) item.RemotePath = remote;
                            }
                        }
                    }
                }
            }
            catch { }
            foreach (DiskState item in mapped.Values)
            {
                string remote = item.RemotePath.Trim();
                // Keep the letter prominent; complete UNC path is on second line/tooltip.
                string host = remote.StartsWith(@"\\")
                    ? remote.TrimStart('\\').Split('\\').FirstOrDefault() : "";
                item.Name = item.DriveLetter + "  " + (host.Length > 0 ? host : "NETWORK SHARE");
                item.VolumeSummary = remote.Length > 0 ? remote : "REMOTE PATH NOT AVAILABLE";
                item.VolumeLabels = DescribeVolume(item.DriveLetter, volumeNames);
                item.Health = item.Connected ? "Connected" : "Unavailable";
            }
            return mapped.Values.ToList();
        }
        private static string SortLetter(DiskState item)
        {
            if (item.IsNetwork) return item.DriveLetter;
            // A multi-partition physical disk is sorted by its alphabetically first
            // assigned letter, not by the arbitrary order returned by WMI.
            string first = Regex.Matches(item.Volumes ?? "", @"[A-Za-z]:")
                .Cast<Match>().Select(match => match.Value.ToUpperInvariant())
                .OrderBy(letter => letter, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return first ?? "ZZZ";
        }
        private void RefreshDiskInventory()
        {
            List<DiskState> disks = new List<DiskState>();
            Dictionary<string, string> volumeNames = ReadVolumeLabels();
            try
            {
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", "SELECT Index,Model,Size FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject o in rows)
                    {
                        using (o)
                        {
                            DiskState x = new DiskState();
                            x.Index = (int)Helpers.Number(Helpers.WmiValue(o, "Index"), -1);
                            x.Name = Helpers.WmiStr(o, "Model", "Disk " + x.Index).Trim();
                            x.SizeGB = Helpers.Number(Helpers.WmiValue(o, "Size"), 0) / 1000000000.0;
                            // Empty zero-capacity generic USB readers are not usable volumes.
                            // Keep them as hardware when an actual medium is present.
                            if (x.Index >= 0) disks.Add(x);
                        }
                    }
                }
                Dictionary<string, int> parts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", "SELECT DeviceID,DiskIndex FROM Win32_DiskPartition"))
                {
                    foreach (ManagementObject o in rows)
                    {
                        using (o) parts[Helpers.WmiStr(o, "DeviceID", "")] = (int)Helpers.Number(Helpers.WmiValue(o, "DiskIndex"), -1);
                    }
                }
                Dictionary<int, List<string>> letters = new Dictionary<int, List<string>>();
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2", "SELECT Antecedent,Dependent FROM Win32_LogicalDiskToPartition"))
                {
                    foreach (ManagementObject o in rows)
                    {
                        using (o)
                        {
                            string antecedent = Helpers.WmiStr(o, "Antecedent", "");
                            string dependent = Helpers.WmiStr(o, "Dependent", "");
                            Match pm = Regex.Match(antecedent, "DeviceID=\"(Disk #[0-9]+, Partition #[0-9]+)\"", RegexOptions.IgnoreCase);
                            Match lm = Regex.Match(dependent, "DeviceID=\"([A-Za-z]:)\"", RegexOptions.IgnoreCase);
                            if (!pm.Success || !lm.Success) continue;
                            int ix;
                            if (!parts.TryGetValue(pm.Groups[1].Value, out ix)) continue;
                            if (!letters.ContainsKey(ix)) letters[ix] = new List<string>();
                            if (!letters[ix].Contains(lm.Groups[1].Value)) letters[ix].Add(lm.Groups[1].Value);
                        }
                    }
                }
                foreach (DiskState d in disks)
                {
                    if (letters.ContainsKey(d.Index))
                    {
                        letters[d.Index].Sort(StringComparer.OrdinalIgnoreCase);
                        foreach (string letter in letters[d.Index])
                        {
                            string known;
                            if (!volumeNames.TryGetValue(letter, out known) || String.IsNullOrWhiteSpace(known))
                            {
                                try
                                {
                                    DriveInfo info = new DriveInfo(letter + "\\");
                                    if (info.IsReady && !String.IsNullOrWhiteSpace(info.VolumeLabel))
                                        volumeNames[letter] = info.VolumeLabel;
                                }
                                catch { }
                            }
                        }
                    }
                    d.Volumes = letters.ContainsKey(d.Index) ? String.Join("  ", letters[d.Index].ToArray()) : "No letter";
                    d.VolumeLabels = letters.ContainsKey(d.Index)
                        ? String.Join(" | ", letters[d.Index].Select(letter => DescribeVolume(letter, volumeNames)).ToArray())
                        : "NO MOUNTED VOLUME";
                    List<string> summary = new List<string>();
                    double usedSum = 0, totalSum = 0;
                    if (letters.ContainsKey(d.Index))
                    {
                        foreach (string letter in letters[d.Index])
                        {
                            try
                            {
                                DriveInfo vol = new DriveInfo(letter + "\\");
                                if (vol.IsReady && vol.TotalSize > 0)
                                {
                                    double totalGB = vol.TotalSize / 1000000000.0;
                                    double usedGB = (vol.TotalSize - vol.TotalFreeSpace) / 1000000000.0;
                                    summary.Add(letter + " " + usedGB.ToString("0") + "/" + totalGB.ToString("0") + "G");
                                    usedSum += usedGB;
                                    totalSum += totalGB;
                                }
                                else summary.Add(letter);
                            }
                            catch { summary.Add(letter); }
                        }
                    }
                    d.VolumeSummary = summary.Count > 0 ? String.Join("  ", summary.ToArray()) : "No mounted volume";
                    d.MountedUsedGB = usedSum;
                    d.MountedTotalGB = totalSum;
                    d.MountedFillPct = totalSum > 0 ? (double?)Helpers.Clamp(100 * usedSum / totalSum, 0, 100) : null;
                }
            }
            catch { }
            // Windows sometimes exposes an unmounted "Microsoft Virtual Disk" as a
            // bare hardware record. Hide only this unused entry from the gadget.
            // Never remove a real mounted volume/letter or change Windows storage.
            disks.RemoveAll(item =>
                Regex.IsMatch(item.Name ?? "", @"\b(?:Microsoft|Msft)\s+Virtual\s+Disk\b", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(item.Volumes ?? "", @"[A-Za-z]:"));
            // One alphabetically sorted list: local hardware and mapped network letters.
            // Unmounted local hardware (including USB readers without media) goes last.
            diskInventory = disks.Concat(ReadNetworkInventory(volumeNames))
                .OrderBy(item => SortLetter(item), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.IsNetwork ? 1 : 0)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Index).ToList();
            diskStamp = DateTime.Now;
        }
        private void RefreshSmart()
        {
            smartStamp = DateTime.Now;
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Smart.ps1");
            if (!File.Exists(script)) return;
            string output = Helpers.Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"", 9000);
            Dictionary<string, string[]> updated = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("DISK|", StringComparison.Ordinal)) continue;
                string[] p = line.Split('|');
                if (p.Length >= 7) updated[p[1].Trim()] = p;
            }
            if (updated.Count > 0)
            {
                smartInfo.Clear();
                foreach (KeyValuePair<string, string[]> x in updated) smartInfo[x.Key] = x.Value;
            }
        }
        private Dictionary<int, double[]> ReadDiskRates()
        {
            Dictionary<int, double[]> rates = new Dictionary<int, double[]>();
            // A rate is valid only if at least one counter provider returns a sample.
            try
            {
                // Raw values are byte COUNTERS, not instantaneous byte/s values.
                // Calculate differences using Windows performance counter timestamps.
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2",
                    "SELECT Name,DiskReadBytesPerSec,DiskWriteBytesPerSec," +
                    "Timestamp_PerfTime,Frequency_PerfTime FROM Win32_PerfRawData_PerfDisk_PhysicalDisk"))
                {
                    foreach (ManagementObject o in rows)
                    {
                        using (o)
                        {
                            string name = Helpers.WmiStr(o, "Name", "");
                            Match match = Regex.Match(name, @"^(\d+)(?:\s|$)");
                            int diskId;
                            if (!match.Success || !int.TryParse(match.Groups[1].Value, out diskId)) continue;
                            ulong read, write, tick, freq;
                            if (!UInt64.TryParse(Convert.ToString(Helpers.WmiValue(o, "DiskReadBytesPerSec")), out read) ||
                                !UInt64.TryParse(Convert.ToString(Helpers.WmiValue(o, "DiskWriteBytesPerSec")), out write) ||
                                !UInt64.TryParse(Convert.ToString(Helpers.WmiValue(o, "Timestamp_PerfTime")), out tick) ||
                                !UInt64.TryParse(Convert.ToString(Helpers.WmiValue(o, "Frequency_PerfTime")), out freq) ||
                                freq == 0) continue;
                            // Keep per-device baseline for the next 1-5-second poll.
                            DiskIoSample now = new DiskIoSample { Read = read, Write = write, Tick = tick, Freq = freq };
                            DiskIoSample old;
                            if (previousDiskIo.TryGetValue(diskId, out old) && tick > old.Tick &&
                                read >= old.Read && write >= old.Write)
                            {
                                double seconds = (tick - old.Tick) / (double)freq;
                                if (seconds > 0.05 && seconds < 300)
                                {
                                    double rd = (read - old.Read) / seconds / 1048576.0;
                                    double wr = (write - old.Write) / seconds / 1048576.0;
                                    // Avoid presenting counter resets / corrupted WMI data as valid.
                                    if (rd >= 0 && wr >= 0 && rd < 64000 && wr < 64000)
                                        rates[diskId] = new double[] { rd, wr };
                                }
                            }
                            previousDiskIo[diskId] = now;
                        }
                    }
                }
            }
            catch { /* Raw WMI perf counters are optional. */ }
            // The first sample has no previous interval. Use Windows' cooked counters
            // only for those entries or if the raw provider is not available.
            try
            {
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2",
                    "SELECT Name,DiskReadBytesPerSec,DiskWriteBytesPerSec " +
                    "FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk"))
                {
                    foreach (ManagementObject o in rows)
                    {
                        using (o)
                        {
                            string name = Helpers.WmiStr(o, "Name", "");
                            Match match = Regex.Match(name, @"^(\d+)(?:\s|$)");
                            int diskId;
                            if (!match.Success || !int.TryParse(match.Groups[1].Value, out diskId) ||
                                rates.ContainsKey(diskId)) continue;
                            double rd = Helpers.Number(Helpers.WmiValue(o, "DiskReadBytesPerSec"), -1);
                            double wr = Helpers.Number(Helpers.WmiValue(o, "DiskWriteBytesPerSec"), -1);
                            if (rd >= 0 && wr >= 0 && rd < 64000 * 1048576.0 && wr < 64000 * 1048576.0)
                                rates[diskId] = new double[] { rd / 1048576.0, wr / 1048576.0 };
                        }
                    }
                }
            }
            catch { /* If both providers fail, show N/A rather than fake zeros. */ }
            return rates;
        }
        private Dictionary<string, double[]> ReadLogicalDiskRates()
        {
            Dictionary<string, double[]> rates = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (ManagementObjectCollection rows = Helpers.Query(@"root\cimv2",
                    "SELECT Name,DiskReadBytesPerSec,DiskWriteBytesPerSec " +
                    "FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk"))
                {
                    foreach (ManagementObject o in rows)
                    {
                        using (o)
                        {
                            string letter = Helpers.WmiStr(o, "Name", "").Trim().ToUpperInvariant();
                            if (!Regex.IsMatch(letter, @"^[A-Z]:$")) continue;
                            double rd = Helpers.Number(Helpers.WmiValue(o, "DiskReadBytesPerSec"), -1);
                            double wr = Helpers.Number(Helpers.WmiValue(o, "DiskWriteBytesPerSec"), -1);
                            if (rd < 0 || wr < 0) continue;
                            rates[letter] = new double[] { rd / 1048576.0, wr / 1048576.0 };
                        }
                    }
                }
            }
            catch { /* Per-volume Windows counters are optional. */ }
            return rates;
        }
        private void ReadDisk(DeckData d)
        {
            if (diskStamp == DateTime.MinValue || (DateTime.Now - diskStamp).TotalSeconds > 30) RefreshDiskInventory();
            if (smartStamp == DateTime.MinValue || (DateTime.Now - smartStamp).TotalSeconds > 120) RefreshSmart();
            Dictionary<int, double[]> perf = ReadDiskRates();
            Dictionary<string, double[]> logicalPerf = ReadLogicalDiskRates();
            List<DiskState> results = new List<DiskState>();
            foreach (DiskState source in diskInventory)
            {
                DiskState disk = new DiskState {
                    Index = source.Index, Name = source.Name, SizeGB = source.SizeGB,
                    Volumes = source.Volumes, VolumeSummary = source.VolumeSummary,
                    VolumeLabels = source.VolumeLabels,
                    MountedTotalGB = source.MountedTotalGB, MountedUsedGB = source.MountedUsedGB,
                    MountedFillPct = source.MountedFillPct,
                    IsNetwork = source.IsNetwork, Connected = source.Connected,
                    DriveLetter = source.DriveLetter, RemotePath = source.RemotePath,
                    Health = source.Health
                };
                if (disk.IsNetwork)
                {
                    // No reliable local per-share disk IO/SMART/temp. Network bandwidth
                    // is measured separately by the Network gadget.
                    results.Add(disk);
                    continue;
                }
                double[] vals;
                if (perf.TryGetValue(disk.Index, out vals))
                {
                    disk.ReadMB = vals[0]; disk.WriteMB = vals[1]; disk.IoAvailable = true;
                }
                // When physical I/O is not available or is stuck at zero, fall
                // back to the sum of mounted local volume counters on this disk.
                // Never use these as a temperature/SMART source or for SMB shares.
                if (!disk.IoAvailable || (disk.ReadMB == 0 && disk.WriteMB == 0))
                {
                    double readSum = 0, writeSum = 0;
                    int available = 0;
                    foreach (Match letter in Regex.Matches(disk.Volumes ?? "", @"[A-Za-z]:"))
                    {
                        double[] logical;
                        if (!logicalPerf.TryGetValue(letter.Value, out logical)) continue;
                        readSum += logical[0]; writeSum += logical[1]; available++;
                    }
                    if (available > 0)
                    {
                        disk.IoAvailable = true;
                        disk.ReadMB = readSum;
                        disk.WriteMB = writeSum;
                    }
                }
                string[] best = null;
                if (!smartInfo.TryGetValue(disk.Index.ToString(), out best))
                {
                    // Do not attach SMART readings to the wrong device when models repeat.
                    string needle = Normalize(disk.Name);
                    string[][] matches = smartInfo.Values.Where(item =>
                        needle.Length >= 6 && Normalize(item[2]).Length >= 6 &&
                        (Normalize(item[2]).Contains(needle) || needle.Contains(Normalize(item[2])))).ToArray();
                    if (matches.Length == 1 && diskInventory.Count(it => Normalize(it.Name) == needle) == 1)
                        best = matches[0];
                }
                if (best != null)
                {
                    disk.Health = best[3];
                    disk.Wear = Helpers.NullableNumber(best[4]);
                    disk.Temp = Helpers.NullableNumber(best[5]);
                    disk.Bus = best[6];
                }
                if (!disk.Temp.HasValue)
                {
                    // LHM name-based disk temperature fallback: only for unique matching models.
                    string needle = Normalize(disk.Name);
                    string[] matches = storageTemperatures.Keys.Where(key =>
                        needle.Length >= 6 && Normalize(key).Length >= 6 &&
                        (Normalize(key).Contains(needle) || needle.Contains(Normalize(key)))).ToArray();
                    double val;
                    if (matches.Length == 1 && diskInventory.Count(it => Normalize(it.Name) == needle) == 1 &&
                        storageTemperatures.TryGetValue(matches[0], out val)) disk.Temp = val;
                }
                results.Add(disk);
            }
            d.Disks = results.ToArray();
        }
    }

    internal sealed class DeckContext : ApplicationContext
    {
        private readonly Dictionary<string, DeckForm> views = new Dictionary<string, DeckForm>();
        private readonly Probe probe = new Probe();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private ToolStripMenuItem lockMenuItem;
        private int cleanupWorking;
        private string cleanupStatus = "";
        private DateTime cleanupFinished = DateTime.MinValue;
        private int working;
        private bool visible = true;
        private bool top = true;
        private bool snap = true;
        private bool locked = false;
        private bool firstLayout = true;
        private bool savedPositions;
        private int interval = 2000;
        private bool layoutDirty;
        private DateTime lastSaved = DateTime.MinValue;
        public volatile DeckData Latest;
        private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
        public bool Top { get { return top; } }
        public bool Snap { get { return snap; } }
        public bool Locked { get { return locked; } }
        public DeckContext()
        {
            menu = new ContextMenuStrip();
            menu.RenderMode = ToolStripRenderMode.System;
            menu.Items.Add("Show / hide gadgets", null, delegate { Toggle(); });
            ToolStripMenuItem atop = new ToolStripMenuItem("Always on top");
            atop.Checked = true;
            atop.CheckOnClick = true;
            atop.CheckedChanged += delegate { top = atop.Checked; foreach (DeckForm f in views.Values) f.TopMost = top; layoutDirty = true; };
            menu.Items.Add(atop);
            ToolStripMenuItem snapItem = new ToolStripMenuItem("Snap to screen edges");
            snapItem.Checked = true;
            snapItem.CheckOnClick = true;
            snapItem.CheckedChanged += delegate { snap = snapItem.Checked; layoutDirty = true; };
            menu.Items.Add(snapItem);
            ToolStripMenuItem lockItem = new ToolStripMenuItem("Lock gadget positions");
            lockItem.CheckOnClick = true;
            lockItem.CheckedChanged += delegate { locked = lockItem.Checked; layoutDirty = true; };
            menu.Items.Add(lockItem);
            lockMenuItem = lockItem;
            ToolStripMenuItem autoStartItem = new ToolStripMenuItem("Start with Windows");
            autoStartItem.CheckOnClick = true;
            autoStartItem.Checked = IsAutoStartEnabled();
            bool updatingAutoStart = false;
            autoStartItem.CheckedChanged += delegate
            {
                if (updatingAutoStart) return;
                if (!SetAutoStart(autoStartItem.Checked))
                {
                    updatingAutoStart = true;
                    autoStartItem.Checked = IsAutoStartEnabled();
                    updatingAutoStart = false;
                    MessageBox.Show("Windows did not permit changing the startup setting.",
                        "NeonDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            menu.Items.Add(autoStartItem);
            ToolStripMenuItem refresh = new ToolStripMenuItem("Refresh interval");
            foreach (int ms in new int[] { 1000, 2000, 5000 })
            {
                int n = ms;
                ToolStripMenuItem item = new ToolStripMenuItem((ms / 1000).ToString() + " s");
                item.Click += delegate { interval = n; timer.Interval = n; layoutDirty = true; foreach (ToolStripMenuItem r in refresh.DropDownItems) r.Checked = (r == item); };
                item.Checked = (ms == 2000);
                refresh.DropDownItems.Add(item);
            }
            menu.Items.Add(refresh);
            menu.Items.Add("Reset positions", null, delegate { ResetPositions(); });
            menu.Items.Add("Sensor help (CPU / disk temps)", null, delegate {
                MessageBox.Show("CPU and disk temperatures require a sensor provider if Windows does not expose them.\n\n"
                    + "You can run portable LibreHardwareMonitor separately, preferably as administrator, "
                    + "with its WMI provider active. NeonDeck reads root\\LibreHardwareMonitor.\n\n"
                    + "Windows Storage provides disk health / wear when supported. "
                    + "N/A means that a reliable reading was not available.",
                    "NeonDeck sensor readings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            menu.Items.Add("Open Task Manager", null, delegate { OpenSystemDetails("CPU"); });
            menu.Items.Add("Windows disks and volumes", null, delegate { OpenSystemDetails("DRIVES"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Empty Recycle Bin now (permanent)", null, delegate { StartCleanup(true); });
            menu.Items.Add("Clean old temporary files (safe scope)", null, delegate { StartCleanup(false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit NeonDeck", null, delegate { ExitThread(); });
            tray = new NotifyIcon();
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information; }
            catch { tray.Icon = SystemIcons.Information; }
            tray.Text = "NeonDeck Desktop Gadgets";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { Toggle(); };
            views["CPU"] = new DeckForm(this, "CPU", 338, 730);
            views["GPU"] = new DeckForm(this, "GPU", 338, 548);
            views["NETWORK"] = new DeckForm(this, "NETWORK", 338, 475);
            views["DRIVES"] = new DeckForm(this, "DRIVES", 338, 486);
            ResetPositions();
            LoadSettings();
            snapItem.Checked = snap;
            lockItem.Checked = locked;
            foreach (DeckForm f in views.Values) { f.TopMost = top; f.Show(); }
            atop.Checked = top;
            foreach (ToolStripMenuItem item in refresh.DropDownItems) item.Checked = item.Text == (interval / 1000).ToString() + " s";
            timer.Interval = interval;
            timer.Tick += delegate
            {
                if (layoutDirty && (DateTime.Now - lastSaved).TotalSeconds >= 2) SaveSettings();
                Kick();
            };
            timer.Start();
            Kick();
        }
        public ContextMenuStrip Menu { get { return menu; } }
        public bool IsCleaning { get { return Interlocked.CompareExchange(ref cleanupWorking, 0, 0) != 0; } }
        public string CleanupStatus { get { return (DateTime.Now - cleanupFinished).TotalSeconds < 60 ? cleanupStatus : ""; } }
        public void MarkLayout() { layoutDirty = true; }
        public void RefreshNow() { Kick(); }
        public void ToggleLock() { lockMenuItem.Checked = !lockMenuItem.Checked; }
        public void OpenDisk(DiskState disk)
        {
            if (disk == null) return;
            if (!disk.IsNetwork) { OpenSystemDetails("DRIVES"); return; }
            try
            {
                // For network shares, open the drive letter in File Explorer.
                // Avoid passing UNC paths as command-line arguments.
                Process.Start(new ProcessStartInfo(disk.DriveLetter + "\\") { UseShellExecute = true });
            }
            catch
            {
                tray.BalloonTipTitle = "NeonDeck network drive";
                tray.BalloonTipText = disk.DriveLetter + " is not accessible. Check your network connection.";
                tray.ShowBalloonTip(3500);
            }
        }
        public void OpenSystemDetails(string gadget)
        {
            string target = gadget == "DRIVES" ? "ms-settings:disksandvolumes" :
                gadget == "NETWORK" ? "ms-settings:network-status" : "taskmgr.exe";
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch
            {
                if (gadget == "DRIVES")
                {
                    try { Process.Start("diskmgmt.msc"); return; } catch { }
                }
                tray.BalloonTipTitle = "NeonDeck";
                tray.BalloonTipText = "Unable to open Windows settings: " + target;
                tray.ShowBalloonTip(3000);
            }
        }
        public void StartCleanup(bool bin)
        {
            if (Interlocked.CompareExchange(ref cleanupWorking, 1, 0) != 0) return;
            cleanupStatus = bin ? "EMPTYING BIN..." : "CLEANING OLD TEMP...";
            DeckForm panel = views["DRIVES"];
            if (!panel.IsDisposed) panel.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string outcome;
                try { outcome = bin ? DeckCleanup.EmptyBin() : DeckCleanup.CleanSafeTemp(); }
                catch (Exception ex) { outcome = "CLEANUP FAILED: " + ex.Message; }
                Interlocked.Exchange(ref cleanupWorking, 0);
                if (panel.IsDisposed || !panel.IsHandleCreated) return;
                try
                {
                    panel.BeginInvoke((MethodInvoker)delegate
                    {
                        if (panel.IsDisposed) return;
                        cleanupStatus = outcome;
                        cleanupFinished = DateTime.Now;
                        panel.Invalidate();
                        tray.BalloonTipTitle = "NeonDeck cleanup";
                        tray.BalloonTipText = outcome;
                        tray.ShowBalloonTip(3500);
                        // Free/used bars are reloaded after cleanup.
                        probe.ForceDiskInventoryRefresh();
                        Kick();
                    });
                }
                catch { }
            });
        }
        private void Kick()
        {
            if (Interlocked.CompareExchange(ref working, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    DeckData next = probe.Poll();
                    Latest = next;
                    DeckForm anchor = views["DRIVES"];
                    if (!anchor.IsDisposed && anchor.IsHandleCreated)
                    {
                        try
                        {
                            anchor.BeginInvoke((MethodInvoker)delegate
                            {
                                if (anchor.IsDisposed) return;
                                bool changed = anchor.AdaptDriveLayout(next.Disks.Length);
                                if (changed)
                                {
                                    bool overlaps = views.Values.Any(f => f != anchor &&
                                        Rectangle.Intersect(anchor.Bounds, f.Bounds).Width > 12 &&
                                        Rectangle.Intersect(anchor.Bounds, f.Bounds).Height > 12);
                                    if (firstLayout && (!savedPositions || overlaps)) ResetPositions();
                                    else anchor.KeepInsideScreen();
                                    MarkLayout();
                                }
                                firstLayout = false;
                                foreach (DeckForm f in views.Values) if (!f.IsDisposed) f.Invalidate();
                            });
                        }
                        catch { }
                    }
                }
                catch { }
                finally { Interlocked.Exchange(ref working, 0); }
            });
        }
        private void Toggle()
        {
            visible = !visible;
            foreach (DeckForm f in views.Values) { if (visible) f.Show(); else f.Hide(); }
        }
        private void ResetPositions()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int gap = 12;
            int pad = 12;
            int y = area.Top + pad;
            DeckForm cpu = views["CPU"], gpu = views["GPU"];
            DeckForm disks = views["DRIVES"], network = views["NETWORK"];
            int rowWidth = cpu.Width + gpu.Width + disks.Width + network.Width + 3 * gap;
            if (rowWidth <= area.Width - 2 * pad)
            {
                // All gadgets are visible across a wide desktop.
                int x = area.Right - pad - rowWidth;
                cpu.Location = new Point(x, y);
                gpu.Location = new Point(x + cpu.Width + gap, y);
                disks.Location = new Point(x + cpu.Width + gap + gpu.Width + gap, y);
                network.Location = new Point(x + cpu.Width + gap + gpu.Width + gap + disks.Width + gap, y);
            }
            else
            {
                // Pair CPU/GPU and drives/network without imposing a fixed drive height.
                int rightX = Math.Max(area.Left + pad, area.Right - pad - cpu.Width);
                int diskX = Math.Max(area.Left + pad, rightX - gap - disks.Width);
                disks.Location = new Point(diskX, y);
                cpu.Location = new Point(rightX, y);
                network.Location = new Point(diskX,
                    Math.Min(area.Bottom - pad - network.Height, y + disks.Height + gap));
                gpu.Location = new Point(rightX,
                    Math.Min(area.Bottom - pad - gpu.Height, y + cpu.Height + gap));
            }
            foreach (DeckForm form in views.Values) form.KeepInsideScreen();
            layoutDirty = true;
        }
        private const string StartupValue = "NeonDeckDesktopGadgets";
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Autostart must use the user-facing NeonDeck.exe GUI launcher, not the cached module.
        private static string StartupExe
        {
            get
            {
                string launcher = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NeonDeck.exe");
                return File.Exists(launcher) ? launcher : Application.ExecutablePath;
            }
        }
        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey, false))
                {
                    string value = key == null ? "" : Convert.ToString(key.GetValue(StartupValue, ""));
                    return value.Trim().Trim('"').Equals(StartupExe, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }
        private static bool SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey))
                {
                    if (key == null) return false;
                    if (enabled) key.SetValue(StartupValue, "\"" + StartupExe + "\"", RegistryValueKind.String);
                    else key.DeleteValue(StartupValue, false);
                }
                return true;
            }
            catch { return false; }
        }
        private void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(settingsPath))
                {
                    string[] p = line.Split('=');
                    if (p.Length != 2) continue;
                    string key = p[0].Trim();
                    string val = p[1].Trim();
                    int n;
                    if (key == "TopMost") top = (val == "true");
                    else if (key == "SnapEdges") snap = (val == "true");
                    else if (key == "Locked") locked = (val == "true");
                    else if (key == "RefreshMs" && int.TryParse(val, out n) && (n == 1000 || n == 2000 || n == 5000)) interval = n;
                    else if (views.ContainsKey(key))
                    {
                        string[] xy = val.Split(',');
                        int x, y;
                        if (xy.Length == 2 && int.TryParse(xy[0], out x) && int.TryParse(xy[1], out y))
                        {
                            Point pos = new Point(x, y);
                            bool onScreen = Screen.AllScreens.Any(scr => scr.WorkingArea.IntersectsWith(new Rectangle(pos, new Size(150, 100))));
                            if (onScreen) { views[key].Location = pos; savedPositions = true; }
                        }
                    }
                }
            }
            catch { }
            layoutDirty = false;
        }
        private void SaveSettings()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("; NeonDeck portable settings (edit or delete to reset)");
                lines.Add("TopMost=" + (top ? "true" : "false"));
                lines.Add("SnapEdges=" + (snap ? "true" : "false"));
                lines.Add("Locked=" + (locked ? "true" : "false"));
                lines.Add("RefreshMs=" + interval);
                foreach (var e in views) lines.Add(e.Key + "=" + e.Value.Left + "," + e.Value.Top);
                File.WriteAllLines(settingsPath, lines.ToArray(), Encoding.UTF8);
                layoutDirty = false;
                lastSaved = DateTime.Now;
            }
            catch { }
        }
        protected override void ExitThreadCore()
        {
            timer.Stop();
            SaveSettings();
            tray.Visible = false;
            tray.Dispose();
            foreach (DeckForm f in views.Values) f.Dispose();
            menu.Dispose();
            base.ExitThreadCore();
        }
    }

    internal static class Art
    {
        public static readonly Color Base = Color.FromArgb(11, 22, 26);
        public static readonly Color Inner = Color.FromArgb(7, 17, 21);
        public static readonly Color Light = Color.FromArgb(213, 235, 220);
        public static readonly Color Dim = Color.FromArgb(101, 155, 151);
        public static readonly Color Mint = Color.FromArgb(105, 230, 151);
        public static readonly Color Aqua = Color.FromArgb(76, 214, 196);
        public static readonly Color Amber = Color.FromArgb(245, 196, 39);
        public static readonly Color Red = Color.FromArgb(234, 81, 66);
        public static readonly Font Small = new Font("Consolas", 8.0f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Mid = new Font("Consolas", 9.1f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Bold = new Font("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Title = new Font("Consolas", 13.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Large = new Font("Consolas", 20.0f, FontStyle.Bold, GraphicsUnit.Point);
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        public static void T(Graphics g, string s, float x, float y, Color c, Font f, float width)
        {
            using (SolidBrush brush = new SolidBrush(c))
            using (StringFormat fmt = new StringFormat())
            {
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s ?? "", f, brush, new RectangleF(x, y, Math.Max(3, width), f.GetHeight(g) + 3), fmt);
            }
        }
        public static void Right(Graphics g, string s, float right, float y, Color c, Font f)
        {
            SizeF size = g.MeasureString(s, f);
            T(g, s, right - size.Width, y, c, f, size.Width + 3);
        }
        public static void Line(Graphics g, int x1, int y1, int x2, int y2, Color c, float stroke)
        {
            using (Pen pen = new Pen(c, stroke)) g.DrawLine(pen, x1, y1, x2, y2);
        }
        public static void Bar(Graphics g, int x, int y, int w, int h, double pct, Color color)
        {
            using (SolidBrush back = new SolidBrush(Color.FromArgb(36, 54, 53))) g.FillRectangle(back, x, y, w, h);
            int full = (int)((w - 1) * Helpers.Clamp(pct, 0, 100) / 100);
            if (full > 0) using (SolidBrush fill = new SolidBrush(color)) g.FillRectangle(fill, x, y, full, h);
            Line(g, x, y + h + 1, x + w, y + h + 1, Color.FromArgb(40, 103, 96), 1);
        }
        public static void Sub(Graphics g, string s, int y, int width)
        {
            Line(g, 17, y + 7, 29, y + 7, Amber, 2);
            T(g, s, 35, y, Dim, Small, width - 45);
        }
        public static void Graph(Graphics g, float[] vals, float[] vals2, int x, int y, int w, int h, float max, Color c1, Color c2)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(8, 26, 28))) g.FillRectangle(b, x, y, w, h);
            for (int i = 1; i <= 4; i++) Line(g, x, y + i * h / 4, x + w, y + i * h / 4, Color.FromArgb(27, 63, 63), 1);
            for (int i = 0; i <= 8; i++) Line(g, x + i * w / 8, y, x + i * w / 8, y + h, Color.FromArgb(20, 62, 62), 1);
            Plot(g, vals, x, y, w, h, max, c1);
            if (vals2 != null) Plot(g, vals2, x, y, w, h, max, c2);
            using (Pen border = new Pen(Color.FromArgb(57, 104, 90))) g.DrawRectangle(border, x, y, w, h);
        }
        private static void Plot(Graphics g, float[] vals, int x, int y, int w, int h, float max, Color color)
        {
            if (vals == null || vals.Length < 2) return;
            PointF[] pts = new PointF[vals.Length];
            for (int i = 0; i < vals.Length; i++)
            {
                float xx = x + (w - 1) * i / 63f;
                float yy = y + h - 2 - (h - 4) * (float)Helpers.Clamp(vals[i] / Math.Max(1, max), 0, 1);
                pts[i] = new PointF(xx, yy);
            }
            using (Pen p = new Pen(color, 1.9f)) g.DrawLines(p, pts);
        }
        public static void Gauge(Graphics g, int x, int y, int d, double? value, string legend, Color color)
        {
            using (Pen track = new Pen(Color.FromArgb(43, 74, 69), 7)) g.DrawArc(track, x, y, d, d, 130, 280);
            if (value.HasValue)
            {
                using (Pen progress = new Pen(color, 7))
                {
                    progress.StartCap = LineCap.Round;
                    progress.EndCap = LineCap.Round;
                    g.DrawArc(progress, x, y, d, d, 130, (float)(280 * Helpers.Clamp(value.Value, 0, 100) / 100));
                }
            }
            string v = value.HasValue ? value.Value.ToString("0") + "%" : "N/A";
            SizeF size = g.MeasureString(v, Bold);
            T(g, v, x + d / 2f - size.Width / 2f, y + d / 2f - 11, color, Bold, size.Width + 3);
            T(g, legend, x - 7, y + d + 9, Dim, Small, d + 40);
        }
        public static void FlatButton(Graphics g, string label, Rectangle r, bool active)
        {
            Color border = active ? Amber : Color.FromArgb(71, 128, 118);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(25, 47, 47))) g.FillRectangle(b, r);
            using (Pen p = new Pen(border)) g.DrawRectangle(p, r);
            T(g, label, r.Left + 5, r.Top + 3, active ? Amber : Aqua, Small, r.Width - 8);
        }
        public static void Frame(Graphics g, string type, int w, int h, bool locked)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(15, 25, 32));
            using (GraphicsPath outer = Round(new RectangleF(1, 1, w - 3, h - 3), 6f))
            {
                using (SolidBrush b = new SolidBrush(Base)) g.FillPath(b, outer);
                using (Pen edge = new Pen(Color.FromArgb(163, 148, 119), 3)) g.DrawPath(edge, outer);
            }
            using (LinearGradientBrush top = new LinearGradientBrush(new Rectangle(12, 11, w - 24, 25), Color.FromArgb(252, 207, 57), Color.FromArgb(175, 130, 27), 90f))
                g.FillRectangle(top, 12, 11, w - 24, 25);
            using (Pen ink = new Pen(Color.FromArgb(25, 30, 29), 4))
            {
                for (int i = 0; i < 4; i++) g.DrawLine(ink, w - 45 + i * 9, 33, w - 29 + i * 9, 13);
            }
            T(g, "▣ " + type + " · NEONDECK", 19, 15, Color.FromArgb(24, 34, 31), Bold, w - 83);
            Line(g, 12, 38, w - 12, 38, Color.FromArgb(76, 90, 75), 1);
            string[] buttonNames = { "MAIN", "AUX", "FENCE", "CTRL" };
            int[] buttonX = { 20, 94, 157, 237 };
            for (int i = 0; i < 4; i++)
            {
                Rectangle r = new Rectangle(buttonX[i], 44, 9, 8);
                Color ledColor = i == 0 ? Red : (i == 2 && locked ? Mint : Color.FromArgb(70, 90, 89));
                using (SolidBrush led = new SolidBrush(ledColor)) g.FillRectangle(led, r);
                Line(g, r.X, r.Bottom + 2, r.Right, r.Bottom + 2, Color.FromArgb(220, 185, 118), 1);
                T(g, buttonNames[i], buttonX[i] + 14, 42, Dim, Small, 46);
            }
            for (int i = 62; i < h - 10; i += 4) Line(g, 10, i, w - 10, i, Color.FromArgb(10, 77, 80, 77), 1);
            // Subtle scuffed-machine casing rivets.
            using (SolidBrush metal = new SolidBrush(Color.FromArgb(102, 102, 87)))
            {
                g.FillEllipse(metal, 5, 42, 3, 7);
                g.FillEllipse(metal, w - 8, h - 32, 3, 7);
            }
        }
    }

    internal sealed class DeckForm : Form
    {
        private readonly DeckContext ctx;
        private readonly string type;
        private int scroll;
        private bool movingByLayout;
        private int driveCols = 1;
        private int driveRows = 1;
        private int drivePitch = 112;
        private readonly ToolTip hoverTip = new ToolTip();
        private string lastHover = "";
        private static readonly int[] HeaderX = { 20, 94, 157, 237 };
        private static readonly Rectangle EmptyBinButton = new Rectangle(120, 82, 94, 21);
        private static readonly Rectangle CleanTempButton = new Rectangle(218, 82, 108, 21);
        private const int DriveColumnWidth = 338;
        private const int DriveHeader = 111;
        private const int DriveFooter = 30;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        public DeckForm(DeckContext context, string kind, int width, int height)
        {
            ctx = context;
            type = kind;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(width, height);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = Art.Base;
            ContextMenuStrip = context.Menu;
            MinimumSize = MaximumSize = Size; // Non-drive panels remain fixed-size.
            AutoScaleMode = AutoScaleMode.None;
            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                int control = HeaderHit(e.Location);
                if (control >= 0)
                {
                    if (control == 0) ctx.RefreshNow();
                    else if (control == 1) ctx.OpenSystemDetails(type);
                    else if (control == 2) { ctx.ToggleLock(); foreach (Form f in Application.OpenForms) f.Invalidate(); }
                    else if (control == 3) ctx.Menu.Show(this, new Point(Math.Min(e.X, Width - 190), 61));
                    return;
                }
                if (type == "DRIVES")
                {
                    if (EmptyBinButton.Contains(e.Location)) { ctx.StartCleanup(true); return; }
                    if (CleanTempButton.Contains(e.Location)) { ctx.StartCleanup(false); return; }
                    int diskNumber = DiskHit(e.Location);
                    DeckData current = ctx.Latest;
                    if (diskNumber >= 0 && current != null && diskNumber < current.Disks.Length)
                    { ctx.OpenDisk(current.Disks[diskNumber]); return; }
                }
                if (!ctx.Locked)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                string hint = "";
                int header = HeaderHit(e.Location);
                if (header == 0) hint = "MAIN: Refresh measurements";
                else if (header == 1) hint = "AUX: Open Windows details";
                else if (header == 2) hint = "FENCE: Lock / unlock gadget positions";
                else if (header == 3) hint = "CTRL: Open NeonDeck menu";
                else if (type == "DRIVES")
                {
                    if (EmptyBinButton.Contains(e.Location)) hint = "Empty all Recycle Bins permanently, no confirmation";
                    else if (CleanTempButton.Contains(e.Location)) hint = "Delete only old temporary files (user Temp 7d / Windows Temp 14d)";
                    else
                    {
                        int hit = DiskHit(e.Location);
                        DeckData current = ctx.Latest;
                        if (hit >= 0 && current != null && hit < current.Disks.Length)
                        {
                            DiskState item = current.Disks[hit];
                            hint = item.IsNetwork
                                ? "Open mapped drive " + item.DriveLetter + " (" + item.RemotePath + ") in Explorer. Volume: " + item.VolumeLabels
                                : "Open Windows Settings > Disks & volumes. Model: " + item.Name + "; Volume(s): " + item.VolumeLabels;
                        }
                    }
                }
                Cursor = hint.Length > 0 ? Cursors.Hand : Cursors.Default;
                if (hint != lastHover) { lastHover = hint; hoverTip.SetToolTip(this, hint); }
            };
            MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if (type == "CPU")
                {
                    scroll = Math.Max(0, scroll + (e.Delta < 0 ? 1 : -1));
                    Invalidate();
                }
            };
            LocationChanged += delegate
            {
                if (!movingByLayout && ctx.Snap) SnapToEdges();
                ctx.MarkLayout();
            };
        }
        private int HeaderHit(Point p)
        {
            for (int i = 0; i < HeaderX.Length; i++)
                if (new Rectangle(HeaderX[i] - 3, 40, i == 2 ? 73 : 65, 18).Contains(p)) return i;
            return -1;
        }
        private int DiskHit(Point p)
        {
            if (type != "DRIVES" || p.Y < DriveHeader || driveRows < 1) return -1;
            int col = p.X / DriveColumnWidth;
            int row = (p.Y - DriveHeader) / drivePitch;
            if (col < 0 || col >= driveCols || row < 0 || row >= driveRows) return -1;
            int localY = (p.Y - DriveHeader) % drivePitch;
            if (localY >= Math.Min(21, Math.Max(10, drivePitch / 4))) return -1;
            int ix = col * driveRows + row;
            DeckData snapshot = ctx.Latest;
            if (snapshot == null || ix >= snapshot.Disks.Length) return -1;
            int x = p.X - col * DriveColumnWidth;
            if (x < 16 || x > DriveColumnWidth - 17) return -1;
            return ix;
        }
        private void SnapToEdges()
        {
            if (movingByLayout) return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int left = Left, top = Top;
            const int threshold = 14;
            if (Math.Abs(left - area.Left) <= threshold) left = area.Left;
            if (Math.Abs(Right - area.Right) <= threshold) left = area.Right - Width;
            if (Math.Abs(top - area.Top) <= threshold) top = area.Top;
            if (Math.Abs(Bottom - area.Bottom) <= threshold) top = area.Bottom - Height;
            if (left != Left || top != Top)
            {
                movingByLayout = true;
                Location = new Point(left, top);
                movingByLayout = false;
            }
        }
        public void KeepInsideScreen()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            if (Location != new Point(x, y))
            {
                movingByLayout = true;
                Location = new Point(x, y);
                movingByLayout = false;
            }
        }
        public bool AdaptDriveLayout(int count)
        {
            if (type != "DRIVES") return false;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            // Vertical, single-column layout is intentional: never spread disks
            // into two or three columns, and never introduce a scrollbar.
            int maxHeight = Math.Max(260, area.Height - 16);
            int desiredCols = 1;
            int desiredRows = Math.Max(1, count);
            int freeHeight = Math.Max(1, maxHeight - DriveHeader - DriveFooter);
            int desiredPitch = Math.Min(112, Math.Max(1, freeHeight / desiredRows));
            int width = DriveColumnWidth;
            int height = Math.Min(maxHeight, DriveHeader + desiredRows * desiredPitch + DriveFooter);
            bool changed = driveCols != desiredCols || driveRows != desiredRows ||
                           drivePitch != desiredPitch || ClientSize.Width != width || ClientSize.Height != height;
            driveCols = desiredCols;
            driveRows = desiredRows;
            drivePitch = desiredPitch;
            if (changed)
            {
                movingByLayout = true;
                MinimumSize = new Size(0, 0);
                MaximumSize = new Size(0, 0);
                ClientSize = new Size(width, height);
                movingByLayout = false;
            }
            return changed;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Art.Frame(g, type, ClientSize.Width, ClientSize.Height, ctx.Locked);
            DeckData d = ctx.Latest;
            if (d == null)
            {
                Art.T(g, "PROBING HARDWARE...", 20, 98, Art.Mint, Art.Bold, Width - 35);
                Art.T(g, "Live metrics will appear shortly.", 20, 126, Art.Dim, Art.Small, Width - 35);
                return;
            }
            if (type == "CPU") Cpu(g, d);
            else if (type == "GPU") Gpu(g, d);
            else if (type == "NETWORK") Network(g, d);
            else if (type == "DRIVES") Drives(g, d);
            string footer = type == "DRIVES" && ctx.CleanupStatus.Length > 0
                ? ctx.CleanupStatus : "NEONDECK  //  " + d.Collected.ToString("HH:mm:ss");
            Art.T(g, footer, 18, Height - 28, Art.Dim, Art.Small, Width - 33);
        }
        private void Cpu(Graphics g, DeckData d)
        {
            Art.T(g, d.CpuName, 18, 67, Art.Light, Art.Bold, 232);
            Art.Right(g, d.CpuLoad.ToString("0") + "%", Width - 17, 62, Art.Mint, Art.Large);
            Art.T(g, "CLOCK  " + (d.CpuClockMHz > 0 ? (d.CpuClockMHz / 1000).ToString("0.00") + " GHz" : "N/A"), 18, 94, Art.Dim, Art.Mid, Width - 36);
            Art.Right(g, "TEMP  " + Helpers.Show(d.CpuTemp, "°C", "0"), Width - 18, 111, Art.Mint, Art.Mid);
            Art.Sub(g, "CPU LOAD HISTORY", 138, Width);
            Art.Graph(g, d.CpuHistory, null, 18, 160, Width - 36, 79, 100, Art.Amber, Art.Aqua);
            Art.T(g, "0%", 19, 242, Art.Dim, Art.Small, 60);
            Art.Right(g, "100%", Width - 19, 242, Art.Dim, Art.Small);
            Art.Sub(g, "BUSIEST PROGRAMS", 265, Width);
            // Five entries fit in the existing CPU pane without shifting RAM or the threads.
            for (int i = 0; i < 5; i++)
            {
                int y = 282 + 15 * i;
                if (i < d.Top.Length)
                {
                    Art.T(g, (i + 1) + "  " + d.Top[i].Name, 18, y, Art.Light, Art.Small, 225);
                    Art.Right(g, d.Top[i].Load.ToString("0.0") + "%", Width - 18, y, Art.Mint, Art.Small);
                }
                else Art.T(g, "--", 20, y, Art.Dim, Art.Small, 110);
            }
            double used = d.RamTotal > 0 ? 100 * d.RamUsed / d.RamTotal : 0;
            Art.Sub(g, "RAM  " + d.RamUsed.ToString("0.0") + " / " + d.RamTotal.ToString("0.0") + " GB", 360, Width);
            Art.Right(g, used.ToString("0") + "%", Width - 19, 359, Art.Amber, Art.Mid);
            Art.Bar(g, 18, 380, Width - 36, 9, used, Art.Amber);
            Art.Sub(g, "LOGICAL PROCESSORS  " + d.Threads.Length + "  (WHEEL TO SCROLL)", 405, Width);
            int max = Math.Max(1, (Height - 472) / 16);
            scroll = Math.Min(scroll, Math.Max(0, d.Threads.Length - max));
            for (int i = 0; i < max; i++)
            {
                int ix = scroll + i;
                if (ix >= d.Threads.Length) break;
                int y = 428 + i * 16;
                Art.T(g, "T" + (ix + 1).ToString("00"), 20, y - 3, Art.Dim, Art.Small, 45);
                Art.Bar(g, 65, y, Width - 133, 6, d.Threads[ix], i % 2 == 0 ? Art.Mint : Art.Aqua);
                Art.Right(g, d.Threads[ix].ToString("0") + "%", Width - 19, y - 4, Art.Light, Art.Small);
            }
            Art.T(g, d.CpuTemp.HasValue ? "CPU TEMPERATURE: SENSOR ACTIVE" : "CPU TEMP: OPTIONAL SENSOR REQUIRED", 19, Height - 49, Art.Dim, Art.Small, Width - 37);
        }
        private void Gpu(Graphics g, DeckData d)
        {
            Art.T(g, d.GpuName, 18, 67, Art.Light, Art.Bold, Width - 102);
            Art.Right(g, Helpers.Show(d.GpuLoad, "%", "0"), Width - 17, 62, Art.Mint, Art.Large);
            Art.T(g, "CORE " + Helpers.Show(d.GpuClock, " MHz", "0") + "  MEM " + Helpers.Show(d.GpuMemClock, " MHz", "0"), 18, 94, Art.Dim, Art.Small, Width - 36);
            Art.Right(g, Helpers.Show(d.GpuTemp, "°C", "0"), Width - 17, 111, Art.Mint, Art.Mid);
            Art.Sub(g, "GPU LOAD HISTORY", 137, Width);
            Art.Graph(g, d.GpuHistory, null, 18, 161, Width - 36, 73, 100, Art.Amber, Art.Aqua);
            Art.Gauge(g, 56, 248, 69, d.GpuLoad, "GPU load", Art.Mint);
            Art.Gauge(g, 208, 248, 69, d.GpuTemp.HasValue ? (double?)Helpers.Clamp(d.GpuTemp.Value, 0, 100) : null, "Temperature", Art.Mint);
            Art.Sub(g, "VRAM  " + (d.VramUsed.HasValue ? d.VramUsed.Value.ToString("0") : "--") + " / " +
                (d.VramTotal.HasValue ? d.VramTotal.Value.ToString("0") : "--") + " MiB", 354, Width);
            Art.Bar(g, 18, 376, Width - 36, 8, d.VramTotal.HasValue && d.VramTotal.Value > 0 && d.VramUsed.HasValue ? 100 * d.VramUsed.Value / d.VramTotal.Value : 0, Art.Aqua);
            Art.Sub(g, "POWER  " + Helpers.Show(d.GpuPower, " W", "0") + " / " + Helpers.Show(d.GpuPowerLimit, " W", "0"), 394, Width);
            Art.Bar(g, 18, 415, Width - 36, 7, d.GpuPower.HasValue && d.GpuPowerLimit.HasValue && d.GpuPowerLimit.Value > 0 ? 100 * d.GpuPower.Value / d.GpuPowerLimit.Value : 0, Art.Amber);
            Art.T(g, "FAN", 18, 433, Art.Dim, Art.Small, 60);
            Art.Right(g, Helpers.Show(d.GpuFan, "%", "0"), Width - 18, 433, Art.Light, Art.Small);
            Art.Bar(g, 18, 453, Width - 36, 6, d.GpuFan ?? 0, Art.Amber);
            Art.T(g, "ENCODER", 18, 467, Art.Dim, Art.Small, 100);
            Art.Right(g, Helpers.Show(d.GpuEncoder, "%", "0"), Width - 18, 467, Art.Light, Art.Small);
            Art.Bar(g, 18, 486, Width - 36, 6, d.GpuEncoder ?? 0, Art.Aqua);
            Art.T(g, "PCIe " + d.GpuPcie + "   DRIVER " + (d.GpuDriver.Length > 0 ? d.GpuDriver : "N/A"), 18, 504, Art.Dim, Art.Small, Width - 36);
        }
        private void Network(Graphics g, DeckData d)
        {
            Art.T(g, d.NicName, 18, 66, Art.Light, Art.Bold, Width - 35);
            Art.T(g, "LINK  " + d.NicLink, 18, 91, Art.Dim, Art.Mid, Width - 36);
            Art.T(g, "↓ " + d.DownMbps.ToString("0.00") + " Mbit/s", 18, 123, Art.Mint, Art.Title, Width - 36);
            Art.T(g, "↑ " + d.UpMbps.ToString("0.00") + " Mbit/s", 18, 153, Art.Aqua, Art.Title, Width - 36);
            Art.Sub(g, "LIVE BANDWIDTH", 195, Width);
            float scale = 10;
            if (d.DownHistory.Length > 0) scale = Math.Max(scale, d.DownHistory.Max() * 1.15f);
            if (d.UpHistory.Length > 0) scale = Math.Max(scale, d.UpHistory.Max() * 1.15f);
            Art.Graph(g, d.DownHistory, d.UpHistory, 18, 216, Width - 36, 98, scale, Art.Amber, Art.Aqua);
            Art.Right(g, "SCALE " + scale.ToString("0") + " Mbit/s", Width - 20, 318, Art.Dim, Art.Small);
            Art.Sub(g, "CONNECTION", 342, Width);
            Art.T(g, "SIGNAL  " + d.WifiSignal, 18, 363, Art.Light, Art.Small, Width - 36);
            Art.Right(g, "PING  " + (d.PingMs.HasValue ? d.PingMs.Value + " ms" : "N/A"), Width - 18, 363, Art.Mint, Art.Small);
            Art.T(g, "LOCAL IP  " + d.NicIp, 18, 383, Art.Dim, Art.Small, Width - 36);
            Art.T(g, "SESSION TRAFFIC", 18, 406, Art.Dim, Art.Small, Width - 36);
            Art.T(g, "↓ " + d.TotalDownGB.ToString("0.000") + " GB", 18, 426, Art.Mint, Art.Small, 138);
            Art.Right(g, "↑ " + d.TotalUpGB.ToString("0.000") + " GB", Width - 18, 426, Art.Aqua, Art.Small);
        }
        // Windows reports capacities in bytes; use decimal GB, as for USED/TOTAL.
        // Below 1 GB, show MB (or KB) so small free-space values stay meaningful.
        private static string FriendlyFreeSpace(double gb)
        {
            gb = Math.Max(0, gb);
            if (gb >= 1.0) return gb.ToString("0.00") + " GB";
            if (gb >= 0.001) return (gb * 1000.0).ToString("0") + " MB";
            return (gb * 1000000.0).ToString("0") + " KB";
        }
        private static string FriendlyRate(double valueMB)
        {
            if (valueMB <= 0.000001) return "0 KB/s";
            if (valueMB < 1.0) return (valueMB * 1024.0).ToString("0") + " KB/s";
            return valueMB.ToString("0.0") + " MB/s";
        }
        // At high disk counts, show the same fields in a denser 80px card
        // instead of switching to multiple columns. The small amount of
        // vertical scaling is a last resort on very short displays.
        private void DrawCompactDriveCard(Graphics g, DiskState x, int x0, int top, int right, int pitch)
        {
            GraphicsState state = g.Save();
            int y = top;
            if (pitch < 80)
            {
                g.TranslateTransform(0, top);
                g.ScaleTransform(1f, Math.Max(0.20f, pitch / 80f));
                y = 0;
            }
            int cardHeight = 78;
            Color accent = x.IsNetwork ? Art.Aqua : Art.Amber;
            if (x.MountedFillPct.HasValue && x.MountedFillPct.Value >= 90) accent = Art.Red;
            Rectangle rect = new Rectangle(x0, y - 2, right - x0, cardHeight);
            using (SolidBrush body = new SolidBrush(Color.FromArgb(16, 35, 39))) g.FillRectangle(body, rect);
            using (Pen border = new Pen(Color.FromArgb(76, 121, 116), 1.1f)) g.DrawRectangle(border, rect);
            using (SolidBrush strip = new SolidBrush(accent)) g.FillRectangle(strip, x0, y - 1, 3, cardHeight - 2);
            int left = x0 + 10;
            int r = right - 8;
            int w = r - left;
            Art.T(g, x.IsNetwork ? "NETWORK  " + x.Name : x.Name, left, y, Art.Light, Art.Bold, w);
            string volume = "VOLUME  " + x.VolumeLabels;
            Art.T(g, volume, left, y + 14, Art.Aqua, Art.Small, w);
            string used = x.MountedFillPct.HasValue
                ? "USED " + x.MountedFillPct.Value.ToString("0") + "%   " +
                  x.MountedUsedGB.ToString("0") + " / " + x.MountedTotalGB.ToString("0") + " GB"
                : x.IsNetwork ? "USED N/A  (SHARE OFFLINE)" :
                  (x.SizeGB < 0.1 ? "NO MEDIA / EMPTY USB READER" : "USED N/A  (NO VOLUME)");
            string freeText = x.MountedFillPct.HasValue
                ? "FREE " + FriendlyFreeSpace(x.MountedTotalGB - x.MountedUsedGB) : "";
            int freeWidth = freeText.Length > 0 ? (int)Math.Ceiling(g.MeasureString(freeText, Art.Small).Width) : 0;
            Art.T(g, used, left, y + 28,
                x.MountedFillPct.HasValue && x.MountedFillPct.Value >= 90 ? Art.Red : Art.Amber,
                Art.Small, Math.Max(3, w - freeWidth - (freeWidth > 0 ? 8 : 0)));
            if (freeWidth > 0) Art.Right(g, freeText, r, y + 28, Art.Mint, Art.Small);
            Art.Bar(g, left, y + 44, w, 4, x.MountedFillPct ?? 0,
                !x.MountedFillPct.HasValue ? Art.Dim :
                x.MountedFillPct.Value >= 90 ? Art.Red : x.MountedFillPct.Value >= 75 ? Art.Amber : Art.Mint);
            if (x.IsNetwork)
            {
                Art.T(g, x.Connected ? "SHARE CONNECTED" : "SHARE OFFLINE", left, y + 51,
                    x.Connected ? Art.Mint : Art.Red, Art.Small, w);
                Art.T(g, "TEMP N/A   I/O N/A (NETWORK)", left, y + 63, Art.Dim, Art.Small, w);
            }
            else
            {
                string health = x.Health == "N/A" ? "N/A" : x.Health.ToUpperInvariant();
                string life = x.Wear.HasValue ? Helpers.Clamp(100 - x.Wear.Value, 0, 100).ToString("0") + "%" : "N/A";
                Art.T(g, "HEALTH " + health + "   LIFE " + life, left, y + 51,
                    x.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ? Art.Mint : Art.Dim,
                    Art.Small, w - 95);
                Art.Right(g, "TEMP " + Helpers.Show(x.Temp, "°C", "0"), r, y + 51,
                    x.Temp.HasValue ? Art.Mint : Art.Dim, Art.Small);
                Art.T(g, "READ " + (x.IoAvailable ? FriendlyRate(x.ReadMB) : "N/A"),
                    left, y + 63, Art.Amber, Art.Small, w / 2 - 2);
                Art.Right(g, "WRITE " + (x.IoAvailable ? FriendlyRate(x.WriteMB) : "N/A"),
                    r, y + 63, Art.Aqua, Art.Small);
            }
            g.Restore(state);
        }
        private void Drives(Graphics g, DeckData d)
        {
            int count = d.Disks.Length;
            int networkCount = d.Disks.Count(item => item.IsNetwork);
            int localCount = count - networkCount;
            Art.T(g, "DRIVES " + count + "   LOCAL " + localCount + "  NETWORK " + networkCount,
                18, 65, Art.Light, Art.Bold, Width - 36);
            Art.T(g, "CLICK MODEL TO OPEN >", 18, 86, Art.Dim, Art.Small, 103);
            Art.FlatButton(g, "EMPTY BIN", EmptyBinButton, ctx.IsCleaning);
            Art.FlatButton(g, "CLEAN TEMP", CleanTempButton, ctx.IsCleaning);
            if (count == 0)
            {
                Art.T(g, "No physical or mapped disks detected.", 18, 129, Art.Dim, Art.Mid, Width - 34);
                return;
            }
            int colW = DriveColumnWidth;
            for (int ix = 0; ix < count; ix++)
            {
                DiskState x = d.Disks[ix];
                int col = ix / driveRows;
                int row = ix % driveRows;
                int x0 = col * colW + 16;
                int y = DriveHeader + row * drivePitch;
                int right = col * colW + colW - 15;
                int availableWidth = right - x0;
                if (drivePitch < 108)
                {
                    DrawCompactDriveCard(g, x, x0, y, right, drivePitch);
                    continue;
                }
                // Paint an individual edged card per drive, rather than a barely
                // visible divider. Scale *all* card contents if the screen is tiny.
                GraphicsState state = g.Save();
                int cardHeight = 106;
                Rectangle rect = new Rectangle(x0, y - 3, availableWidth, cardHeight);
                Color accent = x.IsNetwork ? Art.Aqua : Art.Amber;
                if (x.MountedFillPct.HasValue && x.MountedFillPct.Value >= 90) accent = Art.Red;
                using (SolidBrush body = new SolidBrush(Color.FromArgb(16, 35, 39)))
                    g.FillRectangle(body, rect);
                using (Pen border = new Pen(Color.FromArgb(76, 121, 116), 1.1f))
                    g.DrawRectangle(border, rect);
                using (SolidBrush strip = new SolidBrush(accent))
                    g.FillRectangle(strip, x0, y - 2, 3, cardHeight - 1);
                int left = x0 + 10;
                int r = right - 8;
                int w = r - left;
                string caption = x.IsNetwork ? "NETWORK  " + x.Name : x.Name;
                Art.T(g, caption, left, y + 2, Art.Light, Art.Bold, w - 3);
                string volumes = x.IsNetwork ? "VOLUME  " + x.VolumeLabels :
                    (x.Volumes.IndexOf("  ", StringComparison.Ordinal) >= 0 ? "VOLUMES  " : "VOLUME  ") + x.VolumeLabels;
                Art.T(g, volumes, left, y + 22, Art.Aqua, Art.Small, w - 2);
                string used = x.MountedFillPct.HasValue
                    ? "USED " + x.MountedFillPct.Value.ToString("0") + "%   " +
                      x.MountedUsedGB.ToString("0") + " / " + x.MountedTotalGB.ToString("0") + " GB"
                    : x.IsNetwork ? "USED N/A  (SHARE OFFLINE)" :
                      (x.SizeGB < 0.1 ? "NO MEDIA / EMPTY USB READER" : "USED N/A  (NO VOLUME)");
                string freeText = x.MountedFillPct.HasValue
                    ? "FREE " + FriendlyFreeSpace(x.MountedTotalGB - x.MountedUsedGB) : "";
                int freeWidth = freeText.Length > 0 ? (int)Math.Ceiling(g.MeasureString(freeText, Art.Small).Width) : 0;
                Art.T(g, used, left, y + 40,
                    (x.MountedFillPct.HasValue && x.MountedFillPct.Value >= 90) ? Art.Red : Art.Amber,
                    Art.Small, Math.Max(3, w - freeWidth - (freeWidth > 0 ? 8 : 0)));
                if (freeWidth > 0) Art.Right(g, freeText, r, y + 40, Art.Mint, Art.Small);
                Art.Bar(g, left, y + 58, w - 2, 7, x.MountedFillPct ?? 0,
                    !x.MountedFillPct.HasValue ? Art.Dim :
                    x.MountedFillPct.Value >= 90 ? Art.Red : x.MountedFillPct.Value >= 75 ? Art.Amber : Art.Mint);
                if (x.IsNetwork)
                {
                    Art.T(g, x.Connected ? "SHARE CONNECTED" : "SHARE OFFLINE",
                        left, y + 70, x.Connected ? Art.Mint : Art.Red, Art.Small, w - 2);
                    Art.T(g, "TEMP N/A    I/O N/A (NETWORK)", left, y + 87, Art.Dim, Art.Small, w - 2);
                }
                else
                {
                    string health = x.Health == "N/A" ? "N/A" : x.Health.ToUpperInvariant();
                    string life = x.Wear.HasValue ? Helpers.Clamp(100 - x.Wear.Value, 0, 100).ToString("0") + "%" : "N/A";
                    string temperature = Helpers.Show(x.Temp, "°C", "0");
                    Art.T(g, "HEALTH " + health + "   LIFE " + life, left, y + 70,
                        x.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ? Art.Mint : Art.Dim,
                        Art.Small, w - 104);
                    Art.Right(g, "TEMP " + temperature, r, y + 70, x.Temp.HasValue ? Art.Mint : Art.Dim, Art.Small);
                    Art.T(g, "READ " + (x.IoAvailable ? FriendlyRate(x.ReadMB) : "N/A"),
                        left, y + 87, Art.Amber, Art.Small, w / 2 - 2);
                    Art.Right(g, "WRITE " + (x.IoAvailable ? FriendlyRate(x.WriteMB) : "N/A"),
                        r, y + 87, Art.Aqua, Art.Small);
                }
                g.Restore(state);
            }
        }

    }
}
