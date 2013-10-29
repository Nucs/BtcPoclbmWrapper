using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ProcessReadWriteUtils;

namespace BtcPoclbmWrapper {

    public delegate void MhashUpdatedHandler(double mhps);
    public static class Miner {
        //sample for query: poclbm.exe --device=0 --platform=0 --verbose -r1 elibelash.elibelash:qweqwe@api2.bitcoin.cz:8332 
        
        #region Properties and Events

        public static event MhashUpdatedHandler MhashUpdated;

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
            get { return _miner != null && _miner.HasExited == false; } //todo perform test if the process actually exists.
        }

        /// <summary>
        /// Is currently mining
        /// </summary>
        public static bool IsMining {
            get { return IsOpen && (CollectFeedback != false && MhashPerSecond > 0); }
        }

        /// <summary>
        /// The IO Manager for the program, used to send commands and read/bind the/to output of it, including the MhashPerSecond updates
        /// </summary>
        public static ProcessIOManager IOManager {
            get { return io_proc; }
        }

        /// <summary>
        /// Mega hash per second that is being processed by the miner, if inactive - it is 0 or below. (use IsMining prop for this purpose)
        /// </summary>
        public static double MhashPerSecond {
            get { return _megaHashPerSecond; }
            private set { if (!_props_locked) _megaHashPerSecond = value; }
        }

        /// <summary>
        /// Should <see cref="MhashPerSecond"/> be collected from the poclbm process?
        /// True by default.
        /// </summary>
        public static bool CollectFeedback {
            get { return _collectFeedback; }
            set {
                if (IsOpen) throw new InvalidOperationException("Cannot change this property after miner is started. Please close it first.");
                _collectFeedback = value;
                if (value == false)
                    _megaHashPerSecond = -1;
            }
        }
        
        /// <summary>
        /// What is the system architecture, 64 or 32 bit. (as int)
        /// </summary>
        public static readonly int SystemBits;

        private static Process _miner = null; //the miner proc
        private static double _megaHashPerSecond;
        private static bool _props_locked = false; //prevent _megaHashPerSecond writing till started again.
        private static StreamReader _redirected_reader = null;
        private static StreamWriter _redirected_writer = null;
        private static bool _collectFeedback = true;
        private static ProcessIOManager io_proc = null;

        static Miner() {
            SystemBits = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432")) ? 32 : 64;
        }

        #endregion

        #region Methods
        #region Public

        /// <summary>
        /// Starts a controlled poclbm process.
        /// </summary>
        /// <param name="url">The mining pool url. e.g. slush's pool: `stratum.bitcoin.cz`</param>
        /// <param name="port">Mining pool's port. e.g. slush's pool: 3333</param>
        /// <param name="username">Your worker's username. e.g. elibelash.worker1</param>
        /// <param name="password">Your worker's password.</param>
        /// <param name="hide">Should it launch hidden (in the background) or with a window</param>
        /// <param name="arguments">Extra arguments that you want to pass. Please do not pass the following: `--varbose`;</param>
        public static void Start(string url, ushort port, string username, string password, bool hide = true ,string arguments = "-r1") {
            if (IsOpen)
                throw new InvalidOperationException("Unable to start because there is a miner already open");

            _props_locked = false;
            url = url.Replace("http://", "");
            var args = string.Format("http://{0}:{1}@{2}:{3}", username, password, url, port);

            _miner = new Process();
            _miner.Exited += (sender, eventArgs) => Stop(); //self disposer
            
            var StartInfo = new ProcessStartInfo {  FileName = "cmd.exe",
                                                    WindowStyle = hide ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                                                    CreateNoWindow = hide,
                                                    UseShellExecute = false,
                                                    RedirectStandardInput = true,
                                                    RedirectStandardOutput = true  };
            _miner.StartInfo = StartInfo;
            _miner.Start();
            io_proc = new ProcessIOManager(_miner);
            io_proc.StartProcessOutputRead();
            io_proc.StdoutTextRead += reader;
            /*@echo off cd " + AppDomain.CurrentDomain.BaseDirectory + MinerLocation 
                                                    + string.Format("{0} {1} {2}", MinerAppTarget, arguments, args) */
            _redirected_reader = _miner.StandardOutput;
            _redirected_writer = _miner.StandardInput;

            _redirected_writer.WriteLine("@echo off");
            _redirected_writer.WriteLine("cd " + AppDomain.CurrentDomain.BaseDirectory + MinerLocation); //the command you wish to run.....
            _redirected_writer.WriteLine("{0} {1} {2}", MinerAppTarget, arguments, args);
        }

        public static void Stop() {
            if (_miner != null) { //todo rethink this part
                try {
                    if (_miner.HasExited == false)
                        _miner.Kill();
                } catch (Exception) {
                    if (_miner != null)
                        _miner.Close();
                }
                _miner = null;
            }

            if (_redirected_reader != null) {
                _redirected_reader.Dispose();
                _redirected_reader = null;
            }
            if (_redirected_writer != null) {
                _redirected_writer.Dispose();
                _redirected_writer = null;
            }
            io_proc = null;
            //all set to null because non of them have a property for Disposed
            _megaHashPerSecond = -1; //represents that the miner is not mining. wont be updated till the first feedback from miner
            _props_locked = true; //prevents _megaHashPerSecond writing till started again.
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

        #region Private
        private static void reader(string l) { //reads the output of the poclbm
            if (string.IsNullOrEmpty(l) || l.Contains("    ") || l.Equals("\r\n")) //filtering unneeded messages
                return;

            var m = _regex_hash.Match(l);
            if (m.Success) {
                MhashPerSecond = double.Parse(m.Groups["hps"].Value);
                if (MhashUpdated != null)
                    MhashUpdated(MhashPerSecond);
            }
        }
        #endregion
        #endregion

    }
}
