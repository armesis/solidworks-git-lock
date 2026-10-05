using System;
using System.Drawing;
using System.Windows.Forms;

namespace SwGitLock
{
    /// <summary>Asks for a commit message and whether to release locks after pushing.</summary>
    public class PushDialog : Form
    {
        readonly TextBox txtMessage;
        readonly CheckBox chkRelease;
        readonly bool needsMessage;

        public string CommitMessage { get { return txtMessage.Text.Trim(); } }
        public bool ReleaseLocks { get { return chkRelease.Checked; } }

        public PushDialog(string summary, bool needsMessage, bool hasLocks)
        {
            this.needsMessage = needsMessage;

            Text = "Push changes";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 380);

            var lblMsg = new Label { Text = "What did you change? (e.g. \"Thickened motor mount to 4 mm\")", Location = new Point(12, 10), AutoSize = true };
            txtMessage = new TextBox { Location = new Point(12, 30), Size = new Size(436, 60), Multiline = true, ScrollBars = ScrollBars.Vertical, Enabled = needsMessage };

            var lblFiles = new Label { Text = "Files:", Location = new Point(12, 100), AutoSize = true };
            var txtFiles = new TextBox
            {
                Location = new Point(12, 120), Size = new Size(436, 170), Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false, Text = summary, Font = new Font("Consolas", 8.5f)
            };

            chkRelease = new CheckBox
            {
                Text = "Release my locks after pushing (recommended when you're done)",
                Location = new Point(12, 300), AutoSize = true, Checked = hasLocks, Enabled = hasLocks
            };

            var btnOk = new Button { Text = "Push", Location = new Point(292, 340), Size = new Size(75, 28) };
            var btnCancel = new Button { Text = "Cancel", Location = new Point(373, 340), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
            btnOk.Click += OnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lblMsg, txtMessage, lblFiles, txtFiles, chkRelease, btnOk, btnCancel });
        }

        void OnOk(object sender, EventArgs e)
        {
            if (needsMessage && CommitMessage.Length == 0)
            {
                MessageBox.Show(this, "Please describe what you changed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtMessage.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
