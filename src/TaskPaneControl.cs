using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SwGitLock
{
    /// <summary>The "Git Lock" task pane shown inside SOLIDWORKS.</summary>
    public class TaskPaneControl : UserControl
    {
        const string Title = "Git Lock";
        static readonly string[] CadExtensions = { ".sldprt", ".sldasm", ".slddrw" };

        readonly ISldWorks sw;

        Label lblRepo, lblDoc, lblStatus;
        Button btnLock, btnUnlock, btnPull, btnPush, btnRefresh;
        ListView lvLocks;
        TextBox txtLog;
        ToolStripMenuItem miOpen, miUnlock, miForce;
        int layoutRow;

        // Current context
        string root, branch, docPath, docRel, lastRootError;
        bool hasDoc, docTracked;

        // Lock cache for 'root'
        List<LockInfo> locks = new List<LockInfo>();
        bool locksLoaded, locksVerified;
        string locksRoot;
        DateTime locksTime;

        bool busy;

        public TaskPaneControl(ISldWorks sw)
        {
            this.sw = sw;
            BuildUi();
            Git.Log = LogLine;
            LogLine("Using " + Git.Exe);
        }

        // ---- UI construction ----------------------------------------------

        void BuildUi()
        {
            Dock = DockStyle.Fill;
            BackColor = SystemColors.Window;
            Font = new Font("Segoe UI", 9f);

            var top = new TableLayoutPanel();
            top.Dock = DockStyle.Top;
            top.AutoSize = true;
            top.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            top.ColumnCount = 2;
            top.Padding = new Padding(4);
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            lblRepo = MakeLabel(34, FontStyle.Regular);
            lblRepo.ForeColor = SystemColors.GrayText;
            lblDoc = MakeLabel(22, FontStyle.Bold);
            lblStatus = MakeLabel(58, FontStyle.Regular);
            lblStatus.Padding = new Padding(6, 4, 6, 4);

            btnLock = MakeButton("Lock", DoLock);
            btnUnlock = MakeButton("Unlock", DoUnlock);
            btnPull = MakeButton("Get latest", DoPull);
            btnPush = MakeButton("Push changes", DoPush);
            btnRefresh = MakeButton("Refresh", () => UpdateContext(true));

            var header = MakeLabel(26, FontStyle.Bold);
            header.Text = "Locks in this repository";
            header.TextAlign = ContentAlignment.BottomLeft;

            AddRow(top, lblRepo);
            AddRow(top, lblDoc);
            AddRow(top, lblStatus);
            AddRow(top, btnLock, btnUnlock);
            AddRow(top, btnPull, btnPush);
            AddRow(top, btnRefresh);
            AddRow(top, header);

            lvLocks = new ListView();
            lvLocks.Dock = DockStyle.Fill;
            lvLocks.View = System.Windows.Forms.View.Details;
            lvLocks.FullRowSelect = true;
            lvLocks.HideSelection = false;
            lvLocks.MultiSelect = false;
            lvLocks.ShowItemToolTips = true;
            lvLocks.Columns.Add("File", 150);
            lvLocks.Columns.Add("Locked by", 90);
            lvLocks.Columns.Add("Since", 100);
            lvLocks.DoubleClick += (s, e) => RunAction(OpenSelected);

            var menu = new ContextMenuStrip();
            miOpen = new ToolStripMenuItem("Open in SOLIDWORKS", null, (s, e) => RunAction(OpenSelected));
            miUnlock = new ToolStripMenuItem("Unlock", null, (s, e) => RunAction(UnlockSelected));
            miForce = new ToolStripMenuItem("Force unlock (break lock)...", null, (s, e) => RunAction(ForceUnlockSelected));
            menu.Items.AddRange(new ToolStripItem[] { miOpen, miUnlock, miForce });
            menu.Opening += (s, e) =>
            {
                LockInfo l = SelectedLock();
                if (l == null) { e.Cancel = true; return; }
                miUnlock.Enabled = l.IsMine;
                miForce.Enabled = !l.IsMine;
            };
            lvLocks.ContextMenuStrip = menu;

            txtLog = new TextBox();
            txtLog.Dock = DockStyle.Bottom;
            txtLog.Height = 120;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = SystemColors.Window;
            txtLog.Font = new Font("Consolas", 8f);

            // Fill control first so the docked top/bottom controls are laid out before it.
            Controls.Add(lvLocks);
            Controls.Add(top);
            Controls.Add(txtLog);

            Render();
        }

        static Label MakeLabel(int height, FontStyle style)
        {
            var l = new Label();
            l.AutoSize = false;
            l.Height = height;
            l.Dock = DockStyle.Fill;
            l.AutoEllipsis = true;
            l.TextAlign = ContentAlignment.MiddleLeft;
            if (style != FontStyle.Regular) l.Font = new Font("Segoe UI", 9f, style);
            return l;
        }

        Button MakeButton(string text, Action action)
        {
            var b = new Button();
            b.Text = text;
            b.Height = 30;
            b.Dock = DockStyle.Fill;
            b.Click += (s, e) => RunAction(action);
            return b;
        }

        void AddRow(TableLayoutPanel t, Control left, Control right = null)
        {
            t.RowCount = layoutRow + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(left, 0, layoutRow);
            if (right == null) t.SetColumnSpan(left, 2);
            else t.Controls.Add(right, 1, layoutRow);
            layoutRow++;
        }

        public static string WriteIcon()
        {
            string path = Path.Combine(Path.GetTempPath(), "SwGitLock_16x18.bmp");
            using (var bmp = new Bitmap(16, 18, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(60, 60, 60), 2f)) g.DrawArc(pen, 4, 3, 8, 10, 180, 180);
                using (var body = new SolidBrush(Color.FromArgb(232, 150, 20))) g.FillRectangle(body, 2, 8, 12, 9);
                g.FillRectangle(Brushes.Black, 7, 11, 2, 4);
                bmp.Save(path, ImageFormat.Bmp);
            }
            return path;
        }

        // ---- plumbing --------------------------------------------------------

        void LogLine(string s)
        {
            if (txtLog.TextLength > 30000) txtLog.Text = txtLog.Text.Substring(15000);
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss ") + s.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
        }

        void RunAction(Action action)
        {
            if (busy) return;
            busy = true;
            UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;
            foreach (Button b in new[] { btnLock, btnUnlock, btnPull, btnPush, btnRefresh }) b.Enabled = false;
            SetStatus("Working...", StatusKind.Neutral);
            lblStatus.Refresh();
            try
            {
                action();
                UpdateContext(false);
            }
            catch (Exception ex)
            {
                LogLine("Error: " + ex);
                MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false;
                UseWaitCursor = false;
                Cursor.Current = Cursors.Default;
                Render();
            }
        }

        /// <summary>Called by the add-in when the active document changes.</summary>
        public void OnActiveDocChanged()
        {
            if (busy) return;
            try { UpdateContext(false); }
            catch (Exception ex) { LogLine("Error: " + ex.Message); }
            Render();
        }

        void Warn(string msg) { LogLine(msg); MessageBox.Show(this, msg, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        void Info(string msg) { LogLine(msg); MessageBox.Show(this, msg, Title, MessageBoxButtons.OK, MessageBoxIcon.Information); }

        DialogResult Ask(string msg, MessageBoxButtons buttons)
        {
            return MessageBox.Show(this, msg, Title, buttons, MessageBoxIcon.Question);
        }

        // ---- context ----------------------------------------------------------

        void UpdateContext(bool forceLocks)
        {
            var doc = sw.ActiveDoc as ModelDoc2;
            hasDoc = doc != null;
            string path = hasDoc ? doc.GetPathName() : null;
            docPath = string.IsNullOrEmpty(path) ? null : path;

            if (docPath != null)
            {
                string dir = Path.GetDirectoryName(docPath), error;
                root = Git.FindRoot(dir, out error);
                // Log each distinct failure once, so switching documents doesn't flood the log.
                if (error != null && error != lastRootError) LogLine(error);
                lastRootError = error;
            }
            docRel = root != null && docPath != null ? Git.ToRel(root, docPath) : null;

            if (root == null)
            {
                locks.Clear();
                locksLoaded = false;
                locksRoot = null;
                return;
            }

            if (forceLocks || locksRoot != root || (DateTime.Now - locksTime).TotalSeconds > 120) LoadLocks();
            docTracked = docRel != null && Git.IsTracked(root, docRel);
        }

        void LoadLocks()
        {
            branch = Git.Branch(root);
            bool verified;
            List<LockInfo> list = Git.GetLocks(root, out verified);
            locksRoot = root;
            locksTime = DateTime.Now;
            if (list == null)
            {
                locks.Clear();
                locksLoaded = false;
                LogLine("Couldn't read locks from the server (see message above).");
                return;
            }
            locks = list;
            locksLoaded = true;
            locksVerified = verified;
            if (!verified) LogLine("Warning: the server couldn't confirm which locks are yours.");
        }

        LockInfo FindLock(string rel)
        {
            return locks.FirstOrDefault(l => Git.SamePath(l.Path, rel));
        }

        bool IsMine(string rel)
        {
            LockInfo l = FindLock(rel);
            return l != null && l.IsMine;
        }

        static bool IsCad(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return CadExtensions.Contains(ext);
        }

        // ---- rendering ------------------------------------------------------

        enum StatusKind { Neutral, Good, Bad, Warn, Info }

        void SetStatus(string text, StatusKind kind)
        {
            lblStatus.Text = text;
            switch (kind)
            {
                case StatusKind.Good: lblStatus.BackColor = Color.FromArgb(230, 244, 234); lblStatus.ForeColor = Color.FromArgb(19, 115, 51); break;
                case StatusKind.Bad: lblStatus.BackColor = Color.FromArgb(252, 232, 230); lblStatus.ForeColor = Color.FromArgb(179, 38, 30); break;
                case StatusKind.Warn: lblStatus.BackColor = Color.FromArgb(254, 247, 224); lblStatus.ForeColor = Color.FromArgb(138, 90, 0); break;
                case StatusKind.Info: lblStatus.BackColor = Color.FromArgb(232, 240, 254); lblStatus.ForeColor = Color.FromArgb(26, 79, 160); break;
                default: lblStatus.BackColor = SystemColors.Window; lblStatus.ForeColor = SystemColors.GrayText; break;
            }
        }

        void Render()
        {
            bool inRepo = root != null;
            lblRepo.Text = inRepo
                ? "Repository: " + Path.GetFileName(root) + "   (branch: " + branch + ")"
                : "Open a file from your team's Git repository folder.";

            btnPull.Enabled = btnPush.Enabled = inRepo;
            btnRefresh.Enabled = true;
            btnLock.Enabled = btnUnlock.Enabled = false;
            RenderLocks();

            if (!hasDoc) { lblDoc.Text = "No document open"; SetStatus("", StatusKind.Neutral); return; }
            if (docPath == null) { lblDoc.Text = "(unsaved document)"; SetStatus("Save the document inside the repository folder first.", StatusKind.Neutral); return; }

            lblDoc.Text = Path.GetFileName(docPath);
            if (!inRepo && lastRootError != null) { SetStatus("Git couldn't open this folder's repository.\nSee the message in the log below.", StatusKind.Bad); return; }
            if (!inRepo || docRel == null) { SetStatus("This file is not inside a Git repository.", StatusKind.Neutral); return; }
            if (!locksLoaded) { SetStatus("Couldn't reach the server to check locks.\nCheck your connection and click Refresh.", StatusKind.Bad); return; }

            LockInfo l = FindLock(docRel);
            if (l != null && l.IsMine)
            {
                SetStatus("Locked by you - you can edit and save.\nPush your changes when you're done.", StatusKind.Good);
                btnUnlock.Enabled = true;
            }
            else if (l != null)
            {
                SetStatus("Locked by " + l.Owner + " since " + FormatTime(l.LockedAt) + ".\nYou can look, but not save changes.", StatusKind.Bad);
                if (!locksVerified) lblStatus.Text += "\n(Server couldn't confirm if this lock is yours.)";
            }
            else if (!docTracked)
            {
                SetStatus("New file - not in the repository yet.\nNo lock needed; it's added when you push.", StatusKind.Info);
            }
            else
            {
                SetStatus("Not locked (read-only).\nClick Lock before editing.", StatusKind.Warn);
                btnLock.Enabled = true;
            }
        }

        void RenderLocks()
        {
            lvLocks.BeginUpdate();
            lvLocks.Items.Clear();
            foreach (LockInfo l in locks.OrderByDescending(x => x.IsMine).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(Path.GetFileName(l.Path));
                item.SubItems.Add(l.IsMine ? l.Owner + " (you)" : l.Owner);
                item.SubItems.Add(FormatTime(l.LockedAt));
                item.ToolTipText = l.Path;
                item.Tag = l;
                if (l.IsMine) item.ForeColor = Color.FromArgb(19, 115, 51);
                if (docRel != null && Git.SamePath(l.Path, docRel)) item.Font = new Font(lvLocks.Font, FontStyle.Bold);
                lvLocks.Items.Add(item);
            }
            lvLocks.EndUpdate();
        }

        static string FormatTime(string iso)
        {
            DateTime t;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) return iso;
            return t.ToLocalTime().ToString("dd MMM HH:mm");
        }

        LockInfo SelectedLock()
        {
            return lvLocks.SelectedItems.Count == 1 ? lvLocks.SelectedItems[0].Tag as LockInfo : null;
        }

        // ---- SOLIDWORKS helpers -------------------------------------------

        List<ModelDoc2> LoadedDocs()
        {
            var list = new List<ModelDoc2>();
            var docs = sw.GetDocuments() as object[];
            if (docs != null)
                foreach (object o in docs)
                {
                    var d = o as ModelDoc2;
                    if (d != null) list.Add(d);
                }
            return list;
        }

        ModelDoc2 FindLoadedDoc(string fullPath)
        {
            return LoadedDocs().FirstOrDefault(d => Git.SamePath(d.GetPathName(), fullPath));
        }

        bool SaveDoc(ModelDoc2 d)
        {
            int errors = 0, warnings = 0;
            bool ok = d.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!ok) LogLine("Couldn't save " + d.GetPathName() + " (error " + errors + ")");
            return ok;
        }

        static void SetWritable(string fullPath, bool writable)
        {
            if (!File.Exists(fullPath)) return;
            FileAttributes a = File.GetAttributes(fullPath);
            File.SetAttributes(fullPath, writable ? a & ~FileAttributes.ReadOnly : a | FileAttributes.ReadOnly);
        }

        void OpenDoc(string fullPath)
        {
            if (!File.Exists(fullPath)) return;
            int type;
            switch (Path.GetExtension(fullPath).ToLowerInvariant())
            {
                case ".sldprt": type = (int)swDocumentTypes_e.swDocPART; break;
                case ".sldasm": type = (int)swDocumentTypes_e.swDocASSEMBLY; break;
                case ".slddrw": type = (int)swDocumentTypes_e.swDocDRAWING; break;
                default: return;
            }
            int errors = 0, warnings = 0;
            sw.OpenDoc6(fullPath, type, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
        }

        /// <summary>
        /// Git can't overwrite files SOLIDWORKS has loaded. If any of <paramref name="rels"/> are loaded,
        /// asks to close everything; returns the visible documents to reopen afterwards.
        /// </summary>
        bool CloseDocsFor(List<string> rels, out List<string> reopen)
        {
            reopen = new List<string>();
            var targets = new HashSet<string>(rels.Select(r => Git.ToFull(root, r)), StringComparer.OrdinalIgnoreCase);
            List<ModelDoc2> loaded = LoadedDocs();
            List<string> hit = loaded.Select(d => d.GetPathName()).Where(p => targets.Contains(p)).ToList();
            if (hit.Count == 0) return true;

            List<ModelDoc2> dirty = loaded.Where(d => d.GetSaveFlag()).ToList();
            string msg = "These files are being updated, but they're open in SOLIDWORKS:\n  "
                + string.Join("\n  ", hit.Take(8).Select(Path.GetFileName))
                + (hit.Count > 8 ? "\n  ...and " + (hit.Count - 8) + " more" : "")
                + "\n\nSOLIDWORKS will close all documents, update them, then reopen them.";
            if (dirty.Count > 0)
                msg += "\n\nUnsaved changes in " + dirty.Count + " document(s) will be saved first. "
                    + "Read-only documents can't be saved, so their unsaved changes will be lost.";
            if (Ask(msg + "\n\nContinue?", MessageBoxButtons.OKCancel) != DialogResult.OK) return false;

            foreach (ModelDoc2 d in dirty)
                if (!d.IsOpenedReadOnly()) SaveDoc(d);

            // Reopen what the user had in windows, with the active document last so it ends up on top.
            var active = sw.ActiveDoc as ModelDoc2;
            string activePath = active != null ? active.GetPathName() : null;
            reopen = loaded.Where(d => d.Visible).Select(d => d.GetPathName())
                .Where(p => !string.IsNullOrEmpty(p))
                .OrderBy(p => Git.SamePath(p, activePath) ? 1 : 0)
                .ToList();

            sw.CloseAllDocuments(true);
            return true;
        }

        void Reopen(List<string> paths)
        {
            foreach (string p in paths) OpenDoc(p);
        }

        // ---- actions --------------------------------------------------------

        void DoLock()
        {
            if (root == null || docRel == null) return;
            string name = Path.GetFileName(docPath);
            if (!Git.IsTracked(root, docRel))
            {
                Info("\"" + name + "\" is a new file that isn't in the repository yet.\n\nNo lock is needed; it'll be added when you push.");
                return;
            }

            Git.Run(root, "fetch", "--quiet");
            if (Git.HasUpstream(root) && Git.HasIncoming(root, docRel))
            {
                Warn("A teammate has pushed a newer version of \"" + name + "\".\n\n"
                    + "Click \"Get latest\" first, then lock it. Otherwise you'd be editing an old version.");
                return;
            }

            GitResult r = Git.Run(root, "lfs", "lock", docRel);
            LoadLocks();
            if (!r.Ok)
            {
                Warn("Couldn't lock \"" + name + "\":\n\n" + r.Message);
                return;
            }

            if (!Git.IsLockable(root, docRel))
                LogLine("Note: this file type isn't marked 'lockable' in .gitattributes, so teammates don't get read-only protection for it.");

            SetWritable(docPath, true);
            ModelDoc2 doc = FindLoadedDoc(docPath);
            if (doc != null && doc.IsOpenedReadOnly() && !doc.SetReadOnlyState(false))
                Warn("\"" + name + "\" is locked, but SOLIDWORKS couldn't switch it to write access.\nClose and reopen the file.");
            LogLine("Locked " + docRel);
        }

        void DoUnlock()
        {
            LockInfo l = docRel != null ? FindLock(docRel) : null;
            if (l == null || !l.IsMine) return;

            ModelDoc2 doc = FindLoadedDoc(docPath);
            if (doc != null && doc.GetSaveFlag()
                && Ask("\"" + Path.GetFileName(docPath) + "\" has unsaved changes.\n\n"
                    + "Unlocking makes it read-only, so you won't be able to save them. Unlock anyway?",
                    MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            Unlock(l, false);
            LoadLocks();
        }

        bool Unlock(LockInfo l, bool quiet)
        {
            string name = Path.GetFileName(l.Path);
            if (Git.HasLocalChanges(root, l.Path, Git.HasUpstream(root)))
            {
                if (quiet) LogLine("Kept lock on " + l.Path + " (it has changes that aren't pushed).");
                else Warn("You have changes to \"" + name + "\" that aren't pushed yet.\n\n"
                    + "Click \"Push changes\" first; it can release the lock for you afterwards.");
                return false;
            }

            GitResult r = Git.Run(root, "lfs", "unlock", l.Path);
            if (!r.Ok)
            {
                if (!quiet) Warn("Couldn't unlock \"" + name + "\":\n\n" + r.Message);
                return false;
            }

            string full = Git.ToFull(root, l.Path);
            SetWritable(full, false);
            ModelDoc2 doc = FindLoadedDoc(full);
            if (doc != null && !doc.IsOpenedReadOnly()) doc.SetReadOnlyState(true);
            LogLine("Unlocked " + l.Path);
            return true;
        }

        void UnlockSelected()
        {
            LockInfo l = SelectedLock();
            if (l == null || !l.IsMine) return;
            Unlock(l, false);
            LoadLocks();
        }

        void ForceUnlockSelected()
        {
            LockInfo l = SelectedLock();
            if (l == null || l.IsMine) return;
            if (MessageBox.Show(this,
                    "Break " + l.Owner + "'s lock on \"" + l.Path + "\"?\n\n"
                    + "Only do this if they can't unlock it themselves (for example, they left the team) "
                    + "and you've checked they aren't editing it. Any changes they haven't pushed will clash with yours.",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            GitResult r = Git.Run(root, "lfs", "unlock", "--force", "--id=" + l.Id);
            if (!r.Ok) Warn("Couldn't break the lock:\n\n" + r.Message + "\n\nOn most servers only maintainers/admins can do this.");
            LoadLocks();
        }

        void OpenSelected()
        {
            LockInfo l = SelectedLock();
            if (l != null) OpenDoc(Git.ToFull(root, l.Path));
        }

        void DoPull()
        {
            if (root == null) return;
            GitResult f = Git.Run(root, "fetch");
            if (!f.Ok) { Warn("Couldn't reach the server:\n\n" + f.Message); return; }
            if (!Git.HasUpstream(root))
            {
                Info("This branch isn't connected to the server yet. Use \"Push changes\" first.");
                return;
            }

            List<string> incoming = Git.IncomingFiles(root);
            if (incoming.Count == 0) { LogLine("Already up to date."); return; }

            List<string> reopen;
            if (!CloseDocsFor(incoming, out reopen)) return;

            GitResult r = Git.Run(root, "pull", "--rebase", "--autostash");
            if (!r.Ok)
            {
                Git.Run(root, "rebase", "--abort");
                Warn("Get latest failed:\n\n" + r.Message);
            }
            else LogLine("Updated " + incoming.Count + " file(s).");

            Reopen(reopen);
            LoadLocks();
        }

        void DoPush()
        {
            if (root == null) return;

            // 1. Save open work.
            List<ModelDoc2> dirty = LoadedDocs().Where(d => d.GetSaveFlag() && !d.IsOpenedReadOnly()).ToList();
            if (dirty.Count > 0)
            {
                DialogResult a = Ask(dirty.Count + " open document(s) have unsaved changes. Save them before pushing?\n\n"
                    + "(No = push without those unsaved changes.)", MessageBoxButtons.YesNoCancel);
                if (a == DialogResult.Cancel) return;
                if (a == DialogResult.Yes) foreach (ModelDoc2 d in dirty) SaveDoc(d);
            }

            // 2. Stage everything and see what changed.
            GitResult add = Git.Run(root, "add", "-A");
            if (!add.Ok) { Warn("Couldn't prepare your changes:\n\n" + add.Message); return; }
            List<KeyValuePair<char, string>> changes = Git.StagedChanges(root);

            bool upstream = Git.HasUpstream(root);
            if (upstream) Git.Run(root, "fetch", "--quiet");
            int ahead = upstream ? Git.AheadCount(root) : 1;
            if (changes.Count == 0 && ahead == 0) { Info("Nothing to push - you have no changes."); return; }

            // 3. Refuse to push edits to existing CAD files you don't hold the lock for.
            LoadLocks();
            if (!locksLoaded)
            {
                Git.Run(root, "reset", "--quiet");
                Warn("Couldn't check locks with the server, so nothing was pushed. Check your connection and try again.");
                return;
            }
            List<string> unlocked = changes.Where(c => c.Key != 'A' && IsCad(c.Value) && !IsMine(c.Value)).Select(c => c.Value).ToList();
            if (unlocked.Count > 0)
            {
                Git.Run(root, "reset", "--quiet");
                Warn("You changed these files without locking them:\n  "
                    + string.Join("\n  ", unlocked.Take(10))
                    + (unlocked.Count > 10 ? "\n  ...and " + (unlocked.Count - 10) + " more" : "")
                    + "\n\nA teammate may be editing them too, and one of you would lose work. "
                    + "Lock them (if nobody else has), then push again.");
                return;
            }

            // 4. Commit message.
            string summary = changes.Count == 0
                ? "(No new changes - sending " + ahead + " earlier commit(s) to the server.)"
                : string.Join("\r\n", changes.Select(c => StatusWord(c.Key) + "   " + c.Value));
            string message;
            bool release;
            using (var dlg = new PushDialog(summary, changes.Count > 0, locks.Any(l => l.IsMine)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { Git.Run(root, "reset", "--quiet"); return; }
                message = dlg.CommitMessage;
                release = dlg.ReleaseLocks;
            }

            if (changes.Count > 0)
            {
                GitResult c = Git.Run(root, "commit", "-m", message);
                if (!c.Ok)
                {
                    Git.Run(root, "reset", "--quiet");
                    Warn("Couldn't commit:\n\n" + c.Message);
                    return;
                }
            }

            // 5. Bring in teammates' changes, then push.
            var reopen = new List<string>();
            if (upstream)
            {
                List<string> incoming = Git.IncomingFiles(root);
                if (incoming.Count > 0)
                {
                    if (!CloseDocsFor(incoming, out reopen))
                    {
                        Info("Your changes are saved in a local commit, but not pushed. Push again when ready.");
                        return;
                    }
                    GitResult p = Git.Run(root, "pull", "--rebase");
                    if (!p.Ok)
                    {
                        Git.Run(root, "rebase", "--abort");
                        Reopen(reopen);
                        Warn("Couldn't combine your changes with your teammates':\n\n" + p.Message
                            + "\n\nYour work is safe in a local commit. Ask for help before trying again.");
                        return;
                    }
                }
            }

            GitResult push = upstream ? Git.Run(root, "push") : Git.Run(root, "push", "-u", "origin", "HEAD");
            if (!push.Ok)
            {
                Reopen(reopen);
                Warn("Push failed:\n\n" + push.Message + "\n\nYour work is saved in a local commit; try pushing again.");
                return;
            }
            LogLine("Pushed.");

            // 6. Release locks.
            if (release)
            {
                LoadLocks();
                foreach (LockInfo l in locks.Where(x => x.IsMine).ToList()) Unlock(l, true);
            }

            Reopen(reopen);
            LoadLocks();
            Info("Pushed successfully." + (release ? " Your locks have been released." : ""));
        }

        static string StatusWord(char c)
        {
            switch (c)
            {
                case 'A': return "new     ";
                case 'D': return "deleted ";
                case 'M': return "changed ";
                default: return c + "       ";
            }
        }
    }
}
