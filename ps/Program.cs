using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using NDesk.Options;

namespace ps
{
    public class ProcessInformation
    {
        public string Name;
        public int Ppid;
        public string Owner;
        public bool Responding;
        public double Cpu;
        public long Memory;
        public TimeSpan TotalProcessorTime;
        public DateTime StartTime;
        public int Priority;
    }

    class Program
    {
        internal static IntPtr WtsCurrentServerHandle = IntPtr.Zero;

        internal static int WtsUserName = 5;

        static void Main(string[] args)
        {
            var dictionary = new Dictionary<int, ProcessInformation>();

            var currentParameter = "";

            var pids = new List<string>();
            var users = new List<string>();

            var showFull = false;
            var showLong = false;
            var showHelp = false;
            var showAll = false;

            var p = new OptionSet
            {
                {
                    "e|all", "вывести информацию обо всех процессах",
                    v =>
                    {
                        currentParameter = "e";
                        showAll = true;
                    }
                },
                {
                    "p|processes=", "только перечисленные процессы (следом указывается один или несколько {PID})",
                    v =>
                    {
                        currentParameter = "p";
                        pids.Add(v);
                    }
                },
                {
                    "f|full", "вывести полный листинг",
                    v =>
                    {
                        currentParameter = "f";
                        showFull = true;
                    }
                },
                {
                    "l|long", "вывести листинг в длинном формате",
                    v =>
                    {
                        currentParameter = "l";
                        showLong = true;
                    }
                },
                {
                    "u|users=", "только перечисленные пользователи (следом указывается одно или несколько {NAME})",
                    v =>
                    {
                        currentParameter = "u";
                        users.Add(v);
                    }
                },
                {
                    "<>",
                    v =>
                    {
                        switch (currentParameter)
                        {
                            case "p":
                                pids.Add(v);
                                break;
                            case "u":
                                users.Add(v);
                                break;
                        }
                    }
                },
                {
                    "h|help", "окно помощи",
                    v =>
                    {
                        currentParameter = "h";
                        showHelp = v != null;
                    }
                }
            };

            try
            {
                p.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("greet: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `greet --help' for more information.");
                return;
            }

            if (showHelp)
            {
                ShowHelp(p);
                return;
            }

            var procs = Process.GetProcesses();
            var linq = from Process process in procs
                select process;

            if (pids.Count > 0)
            {
                linq = from Process process in procs
                    where pids.Contains(process.Id.ToString())
                    select process;
            }

            var enumerable = linq.ToList();
            foreach (var process in enumerable)
            {
                if (process.ProcessName != "Idle")
                {
                    if (!WTSQuerySessionInformationW(WtsCurrentServerHandle,
                        process.SessionId,
                        WtsUserName,
                        out var answerBytes,
                        out _)) continue;
                    var userName = Marshal.PtrToStringUni(answerBytes);
                    if (userName == "") userName = "SYSTEM";
                    if (!showAll && process.ProcessName != "ps"&& 
                        !users.Contains(userName)&& 
                        !pids.Contains(process.Id.ToString())) continue;

                    try
                    {
                        dictionary.Add(process.Id, new ProcessInformation
                        {
                            Name = process.ProcessName,
                            Owner = userName,
                            Responding = process.Responding,
                            TotalProcessorTime = process.TotalProcessorTime,
                            Memory = process.WorkingSet64,
                            StartTime = process.StartTime,
                            Priority = process.BasePriority
                        });
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
            }

            Thread.Sleep(100);
            foreach (var process in enumerable)
            {
                try
                {
                    var newProcess = Process.GetProcessById(process.Id);
                    dictionary[process.Id].Cpu = Math.Round(
                    (newProcess.TotalProcessorTime.TotalMilliseconds -
                     dictionary[process.Id].TotalProcessorTime.TotalMilliseconds) / 100.0, 1);
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            if (users.Count > 0)
            {
                var newDictionary = new Dictionary<int, ProcessInformation>();
                foreach (var process in dictionary)
                {
                    if (users.Contains(process.Value.Owner)) newDictionary.Add(process.Key, process.Value);
                }

                dictionary = newDictionary;
            }

            var query = "SELECT * FROM Win32_Process";

            var searcher = new ManagementObjectSearcher(query);
            var processList = searcher.Get();

            var lin = from ManagementObject obj in processList
                select obj;

            if (pids.Count > 0)
            {
                lin = from ManagementObject obj in lin
                    where pids.Contains(obj.Properties["ProcessId"].Value.ToString())
                    select obj;
            }

            foreach (var obj in lin)
            {
                try
                {
                    dictionary[int.Parse(obj.Properties["ProcessId"].Value.ToString())].Ppid =
                        int.Parse(obj.Properties["ParentProcessId"].Value.ToString());
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            if (showLong)
            {
                if (showFull)
                {
                    Console.WriteLine("{0,7}|{1,6}|{2,6}|{3,2}|{4,6}|{5,4}|{6,9}|{7,5}|{8,8}|{9}",
                        "OWNER", "PID", "PPID", "C", "RESP", "CPU%", "MEM", "STIME", "TIME", "NAME");
                    for (var i = 0; i < 80; i++) Console.Write('-');
                    Console.WriteLine();

                    foreach (var process in dictionary)
                        Console.WriteLine("{0,7}|{1,6}|{2,6}|{3,2}|{4,6}|{5,4}|{6,9}|{7,5:HH\\:mm}|{8,8:hh\\:mm\\:ss}|{9}",
                            process.Value.Owner,
                            process.Key,
                            process.Value.Ppid,
                            process.Value.Priority,
                            process.Value.Responding,
                            process.Value.Cpu,
                            process.Value.Memory, 
                            process.Value.StartTime, 
                            process.Value.TotalProcessorTime,
                            process.Value.Name);
                    return;
                }
                Console.WriteLine("{0,7}|{1,6}|{2,6}|{3,2}|{4,6}|{5,4}|{6,9}|{7,8}|{8}",
                    "OWNER", "PID", "PPID", "C", "RESP", "CPU%", "MEM", "TIME", "NAME");
                for (var i = 0; i < 75; i++) Console.Write('-');
                Console.WriteLine();

                foreach (var process in dictionary)
                    Console.WriteLine("{0,7}|{1,6}|{2,6}|{3,2}|{4,6}|{5,4}|{6,9}|{7,8:hh\\:mm\\:ss}|{8}",
                        process.Value.Owner,
                        process.Key,
                        process.Value.Ppid,
                        process.Value.Priority,
                        process.Value.Responding,
                        process.Value.Cpu,
                        process.Value.Memory, 
                        process.Value.TotalProcessorTime,
                        process.Value.Name);
                return;
            }

            if (showFull)
            {
                Console.WriteLine("{0,7}|{1,6}|{2,6}|{3,2}|{4,5}|{5,8}|{6}",
                    "OWNER", "PID", "PPID", "C", "STIME", "TIME", "NAME");
                for (var i = 0; i < 60; i++) Console.Write('-');
                Console.WriteLine();

                foreach (var process in dictionary)
                    Console.WriteLine("{0,7}|{1,6}|{2,6}|{3,2}|{4,5:HH\\:mm}|{5,8:hh\\:mm\\:ss}|{6}",
                        process.Value.Owner,
                        process.Key,
                        process.Value.Ppid,
                        process.Value.Priority, 
                        process.Value.StartTime, 
                        process.Value.TotalProcessorTime,
                        process.Value.Name);
                return;
            }

            Console.WriteLine("{0,6}|{1,8}|{2}",
                "PID", "TIME", "NAME");
            for(var i = 0; i < 30; i++) Console.Write('-');
            Console.WriteLine();

            foreach (var process in dictionary)
                Console.WriteLine("{0,6}|{1,8:hh\\:mm\\:ss}|{2}",
                    process.Key, 
                    process.Value.TotalProcessorTime,
                    process.Value.Name);
        }


        private static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: greet [OPTIONS]+ message");
            Console.WriteLine("Greet a list of individuals with an optional message.");
            Console.WriteLine("If no message is specified, a generic greeting is used.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        [DllImport("Wtsapi32.dll")]
        public static extern bool WTSQuerySessionInformationW(
            IntPtr hServer,
            int sessionId,
            int wtsInfoClass,
            out IntPtr ppBuffer,
            out IntPtr pBytesReturned);
    }
}

