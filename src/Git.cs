using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace SwGitLock
{
    public class GitResult
    {
        public int ExitCode;
        public string Output = "";
        public string Error = "";

        public bool Ok { get { return ExitCode == 0; } }

        public string Message
        {
            get
            {
                string s = (Error + "\n" + Output).Trim();
                return s.Length == 0 ? "(no output, exit code " + ExitCode + ")" : s;
            }
        }
    }

    public class LockInfo
    {
        public string Id;
        public string Path;      // repo-relative, forward slashes
        public string Owner;
        public string LockedAt;  // ISO 8601 from the server
        public bool IsMine;
    }

    /// <summary>Thin wrapper around the git / git-lfs command line.</summary>
    public static class Git
    {
        public static Action<string> Log;

        static string exe;

        /// <summary>
        /// git.exe to run. Programs started before Git was installed (or from a stale environment)
        /// may not have it on PATH, so fall back to the standard install locations.
        /// </summary>
        public static string Exe
        {
            get
            {
                if (exe != null) return exe;
                var candidates = new List<string>();
                foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                    if (dir.Trim().Length > 0) candidates.Add(Path.Combine(dir.Trim().Trim('"'), "git.exe"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\cmd\git.exe"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe"));
                foreach (string c in candidates)
                {
                    try { if (File.Exists(c)) return exe = c; }
                    catch (ArgumentException) { }  // malformed PATH entry
                }
                return exe = "git";
            }
        }

        /// <summary>Runs git and writes the command (and any failure) to the log.</summary>
        public static GitResult Run(string workDir, params string[] args)
        {
            return Exec(true, workDir, args);
        }

        /// <summary>Runs git without logging; for frequent read-only queries.</summary>
        public static GitResult Query(string workDir, params string[] args)
        {
            return Exec(false, workDir, args);
        }

        static GitResult Exec(bool log, string workDir, string[] args)
        {
            var sb = new StringBuilder("-c core.quotepath=false");
            foreach (string a in args) sb.Append(' ').Append(Quote(a));

            var psi = new ProcessStartInfo(Exe, sb.ToString());
            psi.WorkingDirectory = workDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            // Never block waiting for a console password prompt; Git Credential Manager still shows its own window.
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            if (log && Log != null) Log("> git " + string.Join(" ", args));

            var result = new GitResult();
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.StandardInput.Close();
                    var err = p.StandardError.ReadToEndAsync();
                    result.Output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    result.Error = err.Result;
                    result.ExitCode = p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                result.ExitCode = -1;
                result.Error = "Could not start git (" + ex.Message + "). Is Git for Windows installed?";
            }

            if (log && Log != null && !result.Ok) Log(result.Message);
            return result;
        }

        // Quoting per the Windows CommandLineToArgvW rules.
        static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                sb.Append(c);
                slashes = 0;
            }
            sb.Append('\\', slashes * 2).Append('"');
            return sb.ToString();
        }

        public static List<string> Lines(string text)
        {
            return text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        }

        // ---- paths --------------------------------------------------------

        /// <summary>
        /// Returns the repository root containing <paramref name="dir"/>, or null. <paramref name="error"/> is set
        /// when git failed for a reason other than "not a git repository" (e.g. dubious ownership, git missing).
        /// </summary>
        public static string FindRoot(string dir, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            GitResult r = Query(dir, "rev-parse", "--show-toplevel");
            if (!r.Ok)
            {
                if (r.Message.IndexOf("not a git repository", StringComparison.OrdinalIgnoreCase) < 0) error = r.Message;
                return null;
            }
            return Path.GetFullPath(r.Output.Trim().Replace('/', '\\'));
        }

        public static string ToRel(string root, string fullPath)
        {
            string f = Path.GetFullPath(fullPath);
            string r = root.TrimEnd('\\') + "\\";
            if (!f.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return null;
            return f.Substring(r.Length).Replace('\\', '/');
        }

        public static string ToFull(string root, string rel)
        {
            return Path.Combine(root, rel.Replace('/', '\\'));
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // ---- repository state --------------------------------------------

        public static string Branch(string root)
        {
            return Query(root, "rev-parse", "--abbrev-ref", "HEAD").Output.Trim();
        }

        public static bool HasUpstream(string root)
        {
            return Query(root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}").Ok;
        }

        public static bool IsTracked(string root, string rel)
        {
            return Query(root, "ls-files", "--error-unmatch", "--", rel).Ok;
        }

        public static bool IsLockable(string root, string rel)
        {
            return Query(root, "check-attr", "lockable", "--", rel).Output.Trim().EndsWith(": set");
        }

        /// <summary>True if the file has uncommitted changes, or commits not yet pushed.</summary>
        public static bool HasLocalChanges(string root, string rel, bool hasUpstream)
        {
            if (Query(root, "status", "--porcelain", "--", rel).Output.Trim().Length > 0) return true;
            return hasUpstream && Query(root, "log", "@{u}..HEAD", "--format=%h", "--", rel).Output.Trim().Length > 0;
        }

        /// <summary>True if the server (as of the last fetch) has a newer version of the file.</summary>
        public static bool HasIncoming(string root, string rel)
        {
            return Query(root, "log", "HEAD..@{u}", "--format=%h", "--", rel).Output.Trim().Length > 0;
        }

        public static List<string> IncomingFiles(string root)
        {
            return Lines(Query(root, "diff", "--name-only", "HEAD...@{u}").Output);
        }

        public static int AheadCount(string root)
        {
            int n;
            return int.TryParse(Query(root, "rev-list", "--count", "@{u}..HEAD").Output.Trim(), out n) ? n : 0;
        }

        /// <summary>Staged changes as (status letter, path); renames are reported as delete + add.</summary>
        public static List<KeyValuePair<char, string>> StagedChanges(string root)
        {
            var list = new List<KeyValuePair<char, string>>();
            foreach (string line in Lines(Query(root, "diff", "--cached", "--name-status", "--no-renames").Output))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0) list.Add(new KeyValuePair<char, string>(line[0], line.Substring(tab + 1)));
            }
            return list;
        }

        // ---- LFS locks ----------------------------------------------------

        /// <summary>
        /// Reads all locks from the server. Returns null if the server can't be reached.
        /// <paramref name="verified"/> is false when the server couldn't tell us which locks are ours.
        /// </summary>
        public static List<LockInfo> GetLocks(string root, out bool verified)
        {
            var json = new JavaScriptSerializer();
            var locks = new List<LockInfo>();

            GitResult r = Run(root, "lfs", "locks", "--verify", "--json");
            if (r.Ok)
            {
                verified = true;
                var d = json.DeserializeObject(r.Output) as Dictionary<string, object>;
                if (d != null)
                {
                    AddLocks(locks, d, "ours", true);
                    AddLocks(locks, d, "theirs", false);
                }
                return locks;
            }

            verified = false;
            r = Run(root, "lfs", "locks", "--json");
            if (!r.Ok) return null;
            var arr = json.DeserializeObject(r.Output) as object[];
            if (arr != null) foreach (object o in arr) locks.Add(ParseLock(o, false));
            return locks;
        }

        static void AddLocks(List<LockInfo> into, Dictionary<string, object> d, string key, bool mine)
        {
            object v;
            var arr = d.TryGetValue(key, out v) ? v as object[] : null;
            if (arr != null) foreach (object o in arr) into.Add(ParseLock(o, mine));
        }

        static LockInfo ParseLock(object o, bool mine)
        {
            var d = (Dictionary<string, object>)o;
            var l = new LockInfo { Id = Str(d, "id"), Path = Str(d, "path"), LockedAt = Str(d, "locked_at"), IsMine = mine };
            object owner;
            var od = d.TryGetValue("owner", out owner) ? owner as Dictionary<string, object> : null;
            l.Owner = od != null ? Str(od, "name") : "?";
            return l;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }
    }
}
