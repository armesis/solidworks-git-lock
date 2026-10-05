# Git Lock: file locking for SOLIDWORKS teams

A free SOLIDWORKS add-in that adds a **Git Lock** tab to the Task Pane. It uses Git + Git LFS
so only one person can edit a part, assembly or drawing at a time.

| Button | What it does |
|---|---|
| **Lock** | Reserves the open file for you. Refuses if a teammate pushed a newer version you don't have yet. Switches the open document from read-only to editable. |
| **Unlock** | Releases the file. Refuses if you have changes that aren't pushed yet. |
| **Get latest** | Downloads teammates' changes. If any of those files are open, closes and reopens them for you. |
| **Push changes** | Saves, asks for a short description, uploads, and (optionally) releases your locks. **Blocks the push if you changed a file you didn't lock.** |
| Lock list | Shows every lock on the team (yours in green). Right-click: open, unlock, or *force unlock* (for when someone left with files locked). |

Files you haven't locked are **read-only** on your PC, so SOLIDWORKS won't let you save over them by accident.

---

## 1. Pick a Git host (free)

The host must support **Git LFS file locking**:

- **GitLab**: supported on the free tier.
- **GitHub**: supported. Check the free LFS storage/bandwidth quota; CAD history grows quickly.
- **Gitea / Forgejo** (self-hosted): supported, and no quotas.
- Azure DevOps does **not** support LFS locking. Don't use it for this.

## 2. One-time team setup (one person)

1. Create an **empty private** project on the host and invite your teammates with a role that can push.
2. On your PC, in a terminal:
   ```
   git lfs install
   git clone <project URL> "C:\CAD\TeamProject"
   ```
3. Copy `repo-template\.gitattributes` and `repo-template\.gitignore` into `C:\CAD\TeamProject`.
4. Move your CAD files into that folder. Use **Pack and Go** from the top assembly so all references come along.
5. Upload everything:
   ```
   cd /d C:\CAD\TeamProject
   git add -A
   git commit -m "Initial import"
   git push -u origin HEAD
   ```

## 3. Each teammate (once per PC)

1. Install **Git for Windows** (it includes Git LFS): https://git-scm.com/download/win
2. In a terminal:
   ```
   git config --global user.name "Your Name"
   git config --global user.email "you@example.com"
   git lfs install
   git clone <project URL> "C:\CAD\TeamProject"
   ```
   The first time, a sign-in window from the host appears. Sign in there.
3. **Close SOLIDWORKS** and double-click `install.cmd` in this folder (it asks for admin rights).
   It builds the add-in against *your* SOLIDWORKS version and registers it.
4. Start SOLIDWORKS → Task Pane on the right → padlock tab **Git Lock**.
   (If it's missing: Tools → Add-Ins → tick **Git Lock** in both columns.)

To remove it: `uninstall.cmd`.

## 4. Daily workflow

1. **Get latest** at the start of every session.
2. Open the file you need → **Lock** → edit → save.
3. **Push changes** when you finish a piece of work. Write what you changed, and leave
   "Release my locks" ticked unless you're continuing tomorrow.
4. Brand-new files need no lock; they're added on your next push.

## 5. Team rules that prevent broken assemblies

- Only open CAD files **from the repository folder**, and keep everything inside it.
- **Rename/move files only from inside SOLIDWORKS** (or SOLIDWORKS Rename), never in Explorer.
- Toolbox: set it to create **copied parts** inside the project folder.
- To edit an assembly's structure (add/move components, mates), lock the **assembly**.
  To change a part's geometry, lock the **part**.
- Don't sit on locks: push and release at the end of the day.

## Troubleshooting

- **"Couldn't reach the server"**: check your internet connection, then click Refresh. The log at the bottom shows Git's exact message.
- **"detected dubious ownership" in the log**: the repo folder was created by an administrator
  process. Run the `git config --global --add safe.directory ...` command Git prints, then click Refresh.
- **A file opens read-only even though I locked it**: close and reopen it.
- **Someone left with files locked**: right-click the lock → *Force unlock*. On most hosts only project maintainers/admins can do this.
- **Build fails because SOLIDWORKS is in a different folder**: from an administrator command prompt run
  `install.cmd "D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"`.

## Project layout

```
src\           C# source (AddIn.cs = SOLIDWORKS entry point, TaskPaneControl.cs = UI and workflow,
               Git.cs = git/git-lfs wrapper, PushDialog.cs)
build.cmd      compiles bin\SwGitLock.dll with the .NET Framework compiler built into Windows
install.cmd    build + register (admin)
uninstall.cmd  unregister (admin)
repo-template\ .gitattributes and .gitignore for your team repository
```
