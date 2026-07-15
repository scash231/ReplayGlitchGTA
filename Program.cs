using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GTAFirewallToggle
{
    class Program
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private const uint VK_F9 = 0x78;
        private const uint VK_F12 = 0x7B;
        private const int WM_HOTKEY = 0x0312;

        private const int HOTKEY_ID_F9 = 1;
        private const int HOTKEY_ID_F12 = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0 || args[0] != "--launched")
            {
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    if (exePath != null)
                    {
                        string dir = System.IO.Path.GetDirectoryName(exePath);
                        string randomName = Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe";
                        string newPath = System.IO.Path.Combine(dir, randomName);
                        
                        System.IO.File.Copy(exePath, newPath, true);
                        
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = newPath,
                            Arguments = "--launched",
                            UseShellExecute = true
                        };
                        
                        if (!IsAdministrator())
                        {
                            psi.Verb = "runas";
                        }
                        
                        Process.Start(psi);
                    }
                }
                catch { }
                return; // Exit the original launcher
            }

            if (!IsAdministrator())
            {
                System.Windows.Forms.MessageBox.Show("This application requires administrator privileges! Please run it as Administrator.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return;
            }

            ShowInfoPopup();


            RegisterHotKey(IntPtr.Zero, HOTKEY_ID_F9, MOD_CONTROL, VK_F9);
            RegisterHotKey(IntPtr.Zero, HOTKEY_ID_F12, MOD_CONTROL, VK_F12);



            AppDomain.CurrentDomain.ProcessExit += (s, e) => 
            {
                UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_F9);
                UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_F12);
                RemoveFirewallRule();
            };

            Console.CancelKeyPress += (s, e) => 
            {
                e.Cancel = false;
            };

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY)
                {
                    int id = msg.wParam.ToInt32();
                    if (id == HOTKEY_ID_F9)
                    {
                        AddFirewallRule();
                        System.Media.SystemSounds.Exclamation.Play();
                    }
                    else if (id == HOTKEY_ID_F12)
                    {
                        RemoveFirewallRule();
                        System.Media.SystemSounds.Asterisk.Play();
                    }
                }

                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void AddFirewallRule()
        {
            try
            {
                RemoveFirewallRule(); // Ensure it doesn't exist before adding
                
                Type policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                dynamic fwPolicy2 = Activator.CreateInstance(policyType);
                
                Type ruleType = Type.GetTypeFromProgID("HNetCfg.FwRule");
                dynamic rule = Activator.CreateInstance(ruleType);
                
                rule.Name = "123456";
                rule.Description = "Block GTA Save";
                rule.Action = 0; // NET_FW_ACTION_BLOCK
                rule.Direction = 2; // NET_FW_RULE_DIR_OUT
                rule.Enabled = true;
                rule.RemoteAddresses = "192.81.241.171";
                
                fwPolicy2.Rules.Add(rule);
            }
            catch { }
        }

        static void RemoveFirewallRule()
        {
            try
            {
                Type policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                dynamic fwPolicy2 = Activator.CreateInstance(policyType);
                fwPolicy2.Rules.Remove("123456");
            }
            catch { }
        }

        static void ShowInfoPopup()
        {
            var form = new System.Windows.Forms.Form()
            {
                Text = "GTA Firewall Toggle Info",
                Size = new System.Drawing.Size(400, 220),
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true
            };

            var label = new System.Windows.Forms.Label()
            {
                Text = "How it works:\n\n1. Press Ctrl + F9 to block the IP (NO SAVING MODE ON).\n2. Press Ctrl + F12 to unblock the IP (NO SAVING MODE OFF).\n3. Keep the program running in the background to use hotkeys.",
                Dock = System.Windows.Forms.DockStyle.Top,
                Height = 110,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Padding = new System.Windows.Forms.Padding(10),
                Font = new System.Drawing.Font("Segoe UI", 9.5f)
            };

            var button = new System.Windows.Forms.Button()
            {
                Text = "yalla",
                DialogResult = System.Windows.Forms.DialogResult.OK,
                Width = 100,
                Height = 35,
                Top = 125,
                Left = 140,
                Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold)
            };

            form.Controls.Add(label);
            form.Controls.Add(button);
            form.AcceptButton = button;

            form.ShowDialog();
        }
    }
}
