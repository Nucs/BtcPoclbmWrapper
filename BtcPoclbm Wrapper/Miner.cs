using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.AccessControl;
using System.Security.Permissions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ProcessReadWriteUtils;
using Timer = System.Timers.Timer;

namespace BtcPoclbmWrapper {
    public delegate void MhashUpdatedHandler(double mhps);
    public delegate void ShareUpdatedHandler(int accepted, int rejected);
    public delegate void MinerCrashedHandler(List<string> logs, string reason);

    public static class Miner {
        //sample for query: poclbm.exe --device=0 --platform=0 --verbose -r1 elibelash.elibelash:qweqwe@api2.bitcoin.cz:8332 
        
        #region Properties and Events
        /// <summary>
        /// Invoked when new information about the current Mhash/s is given.
        /// </summary>
        public static event MhashUpdatedHandler MhashUpdated;
        /// <summary>
        /// Invoked when either rejects or shares increments.
        /// </summary>
        public static event ShareUpdatedHandler SharesUpdated;
        /// <summary>
        /// Invoked when the miner app seemed to crash,
        /// </summary>
        public static event MinerCrashedHandler MinerCrashed; 

        private static string _minerAppTarget = "poclbm.exe";
        private static string _minerLocation = "\\";
        private const string POCLBM_DEVICE_MATCH_PATTERN = @"^\[(?<id>\d+)\]\s+(?<device>.*)\b(?:.*)?$";
        private const string POCLBM_HASH_MATH_PATTERN = @"^(?=.*(\d+\/(?<shares>\d+)))(?=.*((?<rejects>\d+)\/\d+))(?=.*(\D(?<hps>\d+\.\d+)\s[MmKkTtGg]H/s))"; //for: "stratum.bitcoin.cz:3333 [0.260 MH/s (~0 MH/s)] [Rej: 1/2 (0.00%)]"
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
            get { return new FileInfo(MinerLocation + MinerAppTarget); }
        }

        /// <summary>
        /// Returns the path to the file to the miner application. e.g. C:\somedir\poclbm.exe
        /// </summary>
        public static string MinerFilePath {
            get { return MinerLocation + MinerAppTarget; }
        }

        /// <summary>
        /// Wether the process of the miner is open. for working status use <see cref="IsMining"/>.
        /// </summary>
        public static bool IsOpen {
            get { return _miner != null && _miner.HasExited == false; }
        }

        /// <summary>
        /// Is currently mining
        /// </summary>
        public static bool IsMining {
            get { return IsOpen && MhashPerSecond > 0; }
        }

        /// <summary>
        /// The IO Manager for the program, used to send commands and read/bind the/to output of it, including the MhashPerSecond updates
        /// </summary>
        public static ProcessIOManager IOManager {
            get { return io_proc; }
        }

        /// <summary>
        /// Should the output of poclbm.exe be logged (10 last outputs)? It will be passed at MinerCrashed event, otherwise null will be passed. 
        /// Unlike other properties, the log won't be removed after 
        /// by default true
        /// </summary>
        public static bool LogOutput {
            get { return _logOutput; }
            set {
                _logOutput = value;
                if (value == false) {
                    _logs.Clear();
                    _logs = null;
                } else {
                    if (_logs == null)
                        _logs = new List<string>();
                }
            }
        }

        /// <summary>
        /// 10 last outputs from poclbm. does not clear upon calling <see cref="Stop"/>. Controlled by <see cref="LogOutput"/> which is true by default.
        /// </summary>
        public static List<string> Logs {
            get { return _logs; }
        }

        /// <summary>
        /// Mega hash per second that is being processed by the miner, if inactive - it is 0 or below. (use IsMining prop for this purpose)
        /// </summary>
        public static double MhashPerSecond {
            get { return _megaHashPerSecond; }
            private set { _megaHashPerSecond = value; }
        }

        /* Disabled atm.
         * /// <summary>
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
        }*/

        /// <summary>
        /// The shares that were accepted by now from the mining pool.
        /// </summary>
        public static int Shares {
            get { return _shares; }
        }

        /// <summary>
        /// The shares that were accepted by now from the mining pool.
        /// </summary>
        public static int Rejects {
            get { return _rejects; }
        }
        
        /// <summary>
        /// What is the system architecture, 64 or 32 bit. (as int)
        /// </summary>
        public static readonly int SystemBits;

        private static Process _miner = null; //the miner proc
        private static double _megaHashPerSecond = -1d;
        private static bool _collectFeedback = true;
        private static ProcessIOManager io_proc = null;
        private static int _shares = -1;
        private static int _rejects = -1;
        private static bool _logOutput = true;
        private static List<string> _logs;
        private static Timer _no_mine_tmr = new Timer(10000);
        static Miner() {//_miner.Exited wont invoke untill a call on HasEnded at any part of the code has been called (ofc if the proc indeed exited).
            SystemBits = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432")) ? 32 : 64;
            _no_mine_tmr.Elapsed += (sender, eventArgs) => 
            {
                if (_miner == null) return;
                if (MinerCrashed != null)
                    MinerCrashed(_logs, "Poclbm.exe timed out after 10 seconds of no respond.");
                Stop();
            };
        }

        #endregion

        #region Methods
        #region Public


        /// <summary>
        /// Starts a controlled poclbm process.
        /// </summary>
        /// <param name="url">The mining pool url. e.g. slush's pool: `stratum.bitcoin.cz` or `https://stratum.bitcoin.cz` for specific url</param>
        /// <param name="port">Mining pool's port. e.g. slush's pool: 3333</param>
        /// <param name="username">Your worker's username. e.g. elibelash.worker1</param>
        /// <param name="password">Your worker's password.</param>
        /// <param name="hide">Should it launch hidden (in the background) or with a window</param>
        /// <param name="arguments">Extra arguments that you want to pass. Please do not pass the following: `--varbose`;</param>
        public static void Start(string url, ushort port, string username, string password, bool hide = true ,string arguments = "-r1") {
            if (IsOpen)
                throw new InvalidOperationException("Unable to start because there is a miner already open");
            #region logging, url, tmr , args and sinfo preparation

            var url_header = "http://";
            if (url.Contains("://")) {
                url_header = url.Substring(0, url.IndexOf("://", StringComparison.OrdinalIgnoreCase)) + "://";
                url = url.Replace(url_header, "");
            }
            if (_logs == null && LogOutput)
                _logs = new List<string>();
            if (_logs != null && LogOutput == false) {
                _logs.Clear();
                _logs = null;
            }
            
            var args = string.Format("{0}{1}:{2}@{3}:{4}", url_header ,username, password, url, port);
            var sinfo = new ProcessStartInfo {  FileName = "cmd.exe",
                                                    WindowStyle = hide ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                                                    CreateNoWindow = hide,
                                                    UseShellExecute = false,
                                                    RedirectStandardInput = true,
                                                    RedirectStandardOutput = true  };
            #endregion

            _miner = new Process() { StartInfo = sinfo };
            _miner.Exited += (sender, eventArgs) => { _no_mine_tmr.Stop(); Stop(); }; //self disposer on user manual close.
            
            _miner.Start();
            _no_mine_tmr.Start();
            //_miner.Exited wont invoke untill a call on HasEnded at any 
            //part of the code has been called (ofc if the proc indeed exited).
            //for this case, we call IsOpen at a below half-sec interval to give averagly reliable respond to user manually closing the window.
            Task.Run(() => { while (IsOpen) Thread.Sleep(350); }); //this also dies with the program closing.

            io_proc = new ProcessIOManager(_miner);
            io_proc.StartProcessOutputRead();
            io_proc.StdoutTextRead += reader;
            /* @echo off cd " + AppDomain.CurrentDomain.BaseDirectory + MinerLocation + string.Format("{0} {1} {2}", MinerAppTarget, arguments, args) */

            io_proc.WriteStdin("@echo off");
            io_proc.WriteStdin("cd " + AppDomain.CurrentDomain.BaseDirectory + MinerLocation);
            io_proc.WriteStdin(string.Format("{0} {1} {2}", MinerAppTarget, arguments, args));

        }


        public static void Stop() {
            if (_miner != null) {
                try {
                    if (_miner.HasExited == false) 
                        _miner.CloseMainWindow();
                } catch (Exception) {
                    try {
                        if (_miner != null) //after compiling it seemed that gc automatically set it as null after calling Kill(), even if it fails from lack of permission.
                            _miner.Close();
                    } catch {} //silent catching
                }
                _miner = null;
            }

            if (IOManager != null)
                io_proc = null;

            _no_mine_tmr.Stop();
            
            io_proc = null;
            _megaHashPerSecond = -1; //represents that the miner is not mining. wont be updated till the first feedback from miner
            _shares = -1;
            _rejects = -1;
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
            if (string.IsNullOrEmpty(l)) //filtering unneeded messages
                return;

            _no_mine_tmr.Stop(); //aka reset.
            _no_mine_tmr.Start();
            
            if (LogOutput && _logs != null) { //logging
                _logs.Add(l);
                while (_logs.Count > 10)
                    _logs.RemoveAt(10); //remove the 11th item
            }

            //message based error checking, switch-if style.
            if (l.StartsWith("At least one server is required")) {
                if (MinerCrashed != null)
                    MinerCrashed(_logs, "Invalid canidates, please see logs.");
                Stop();
                return;
            }
            if (l.Contains(" authorization failed with")) {
                if (MinerCrashed != null)
                    MinerCrashed(_logs, "Authorization failed with the given canidates");
                Stop();
                return;
            }
            
            var m = _regex_hash.Match(l);
            if (m.Success) {
                //Mhash handling
                MhashPerSecond = double.Parse(m.Groups["hps"].Value);
                if (MhashUpdated != null)
                    MhashUpdated(MhashPerSecond);

                //Shares/Rejects handling
                var changed = false;
                if (m.Groups["shares"].Value.Equals(_shares.ToString(CultureInfo.InvariantCulture)) == false) {
                    _shares = Convert.ToInt32(m.Groups["shares"].Value);
                    changed = true;
                }

                if (m.Groups["rejects"].Value.Equals(_rejects.ToString(CultureInfo.InvariantCulture)) == false) {
                    _rejects = Convert.ToInt32(m.Groups["rejects"].Value);
                    changed = true;
                }

                if (SharesUpdated != null && changed)
                    SharesUpdated(_shares, _rejects);
            }
        }
        #endregion
        #endregion

    }
}
