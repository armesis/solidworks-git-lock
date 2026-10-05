using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

namespace SwGitLock
{
    /// <summary>SOLIDWORKS add-in entry point: hosts the Git Lock task pane.</summary>
    [ComVisible(true)]
    [Guid("6F1C2A4E-8B3D-4E5F-9A7B-2C1D3E4F5A6B")]
    public class AddIn : ISwAddin
    {
        const string Title = "Git Lock";
        const string Description = "Lock, unlock, get latest and push SOLIDWORKS files with Git LFS";

        SldWorks swApp;
        TaskpaneView view;
        TaskPaneControl pane;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            try
            {
                swApp = (SldWorks)ThisSW;
                pane = new TaskPaneControl(swApp);
                view = swApp.CreateTaskpaneView2(TaskPaneControl.WriteIcon(), Title);
                view.DisplayWindowFromHandlex64(pane.Handle.ToInt64());

                swApp.ActiveModelDocChangeNotify += OnActiveModelDocChange;
                swApp.FileOpenPostNotify += OnFileOpenPost;
                pane.OnActiveDocChanged();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Git Lock failed to start:\n\n" + ex, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            if (swApp != null)
            {
                swApp.ActiveModelDocChangeNotify -= OnActiveModelDocChange;
                swApp.FileOpenPostNotify -= OnFileOpenPost;
            }
            if (view != null)
            {
                view.DeleteView();
                Marshal.ReleaseComObject(view);
                view = null;
            }
            if (pane != null)
            {
                pane.Dispose();
                pane = null;
            }
            swApp = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        int OnActiveModelDocChange()
        {
            if (pane != null) pane.OnActiveDocChanged();
            return 0;
        }

        int OnFileOpenPost(string fileName)
        {
            if (pane != null) pane.OnActiveDocChanged();
            return 0;
        }

        // ---- COM registration (run by RegAsm) ------------------------------

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            string guid = "{" + t.GUID.ToString() + "}";
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\" + guid))
            {
                k.SetValue(null, 0);
                k.SetValue("Description", Description);
                k.SetValue("Title", Title);
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\" + guid))
            {
                k.SetValue(null, 1, RegistryValueKind.DWord);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            string guid = "{" + t.GUID.ToString() + "}";
            Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\SolidWorks\Addins\" + guid, false);
            Registry.CurrentUser.DeleteSubKey(@"Software\SolidWorks\AddInsStartup\" + guid, false);
        }
    }
}
