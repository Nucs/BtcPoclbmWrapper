using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bitcoin_Stealth_Miner;
using BtcPoclbmWrapper;

namespace IdleMiner {
    public partial class frmMain : Form {
        public frmMain() {
            InitializeComponent();
            Miner.MinerLocation = "poclbm" + Miner.SystemBits + "\\";
            Application.ApplicationExit += (sender, args) => Miner.Stop(); //always make sure it will close
        }

        private void btnStart_Click(object sender, EventArgs e) {
            try {
                Miner.CollectFeedback = false;
                Miner.MhashUpdated += mhps => Invoke(new MethodInvoker(()=> Text = "IdleMiner - " + mhps + " Mhash/s"));
                Miner.Start("stratum.bitcoin.cz", 3333, "elibelash.elibelash", "qweqwe", false);
                btnStart.Enabled = false;
                btnStop.Enabled = true;
            } catch {} //silent catching

        }

        private void btnStop_Click(object sender, EventArgs e) {
            Miner.Stop();
            btnStop.Enabled = false;
            btnStart.Enabled = true;
        }

        private void dtTime_ValueChanged(object sender, EventArgs e) {

        }
    }
}
