using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BtcPoclbmWrapper;

namespace Bitcoin_Stealth_Miner {
    class Program {
        #region Main
        static void Main(string[] args) {
            Miner.MinerLocation = "poclbm"+Miner.SystemBits+"\\"; //the directory of the poclbm.exe, copy it first or add post-build command to do so.
            Miner.Start("http://stratum.bitcoin.cz", 3333, "elibelash.idleminer", "1234", true, "-r1");
            Miner.MinerCrashed += (logs, reason) => MessageBox.Show(reason+"\n" + ((logs != null && logs.Count > 0) ? string.Join("\n", logs.ToArray()) : ""), "Miner has crashed");
            //Use this method to print the properties://
            new Thread(looper).Start();
            //or:
            Miner.MhashUpdated += mhps => MessageBox.Show("Average Mhash/s:" + mhps + "\nShares:" + Miner.Shares, "Statistics");
            Miner.SharesUpdated += (accepted, rejected) => MessageBox.Show("Average Mhash/s:" + Miner.MhashPerSecond + "\nShares:" + accepted, "Statistics");
            ///////////////////////////////////////////
            Thread.Sleep(15000);
            Miner.Stop();
        }

        private static void looper() {
            while (true) {
                if (Miner.IsMining)
                    MessageBox.Show("Average Mhash/s:" + Miner.MhashPerSecond + "\nShares:" + Miner.Shares, "Statistics");
                Thread.Sleep(10000);
            }
            
        }

        #endregion
    }
}
