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
                Miner.MhashUpdated += mhps => updateTitle();
                Miner.SharesUpdated += (accepted, rejected) => updateTitle();
                Miner.Start("stratum.bitcoin.cz", 3333, "elibelash.elibelash", "qweqwe", true);
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                updateTitle();
            } catch {} //silent catching
        }

        private void btnStop_Click(object sender, EventArgs e) {
            Miner.Stop();
            btnStop.Enabled = false;
            btnStart.Enabled = true;
            this.Text = "IdleMiner";
        }

        private void dtTime_ValueChanged(object sender, EventArgs e) {

        }

        private void updateTitle() {
            if (InvokeRequired) 
                Invoke(new MethodInvoker(() => updateTitle()));
            this.Text = "IdleMiner " + Miner.MhashPerSecond.ToString("0.00") + " Mhash/s - Shares "+Miner.Rejects+"/"+Miner.Shares;

        }
    }
}
