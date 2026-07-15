using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GTAFirewallToggle
{
    class Program
    {
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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

        public const int HOTKEY_ID_F9 = 1;
        public const int HOTKEY_ID_F12 = 2;

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
                        string dir = System.IO.Path.GetTempPath();
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

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            
            // Show the popup on a separate thread so it doesn't block the main message loop
            System.Threading.Tasks.Task.Run(() => ShowInfoPopup());

            System.Windows.Forms.Application.Run(new OverlayForm());
        }

        static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void AddFirewallRule()
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

        public static void RemoveFirewallRule()
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
                Text = "Info",
                Size = new System.Drawing.Size(440, 260),
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

    class OverlayForm : System.Windows.Forms.Form
    {
        private bool _isBlocked = false;

        public OverlayForm()
        {
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = System.Drawing.Color.Magenta;
            this.TransparencyKey = System.Drawing.Color.Magenta;
            this.Size = new System.Drawing.Size(20, 20);
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            var screen = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            this.Location = new System.Drawing.Point(10, screen.Height - 30);
        }

        public void SetBlocked(bool blocked)
        {
            _isBlocked = blocked;
            this.Invalidate();
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var brush = _isBlocked ? System.Drawing.Brushes.Red : System.Drawing.Brushes.LimeGreen;
            e.Graphics.FillEllipse(brush, 0, 0, 16, 16);
        }

        protected override System.Windows.Forms.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20 | 0x80000 | 0x80; // WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == Program.HOTKEY_ID_F9)
                {
                    Program.AddFirewallRule();
                    System.Media.SystemSounds.Exclamation.Play();
                    SetBlocked(true);
                }
                else if (id == Program.HOTKEY_ID_F12)
                {
                    Program.RemoveFirewallRule();
                    System.Media.SystemSounds.Asterisk.Play();
                    SetBlocked(false);
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Exit", null, (s, ev) => System.Windows.Forms.Application.Exit());

            _trayIcon = new System.Windows.Forms.NotifyIcon()
            {
                Icon = System.Drawing.SystemIcons.Shield,
                ContextMenuStrip = contextMenu,
                Text = "GTA Firewall Toggle",
                Visible = true
            };

            // Register Hotkeys when the form loads
            Program.RegisterHotKey(this.Handle, Program.HOTKEY_ID_F9, 0x0002, 0x78); // Ctrl+F9
            Program.RegisterHotKey(this.Handle, Program.HOTKEY_ID_F12, 0x0002, 0x7B); // Ctrl+F12
        }

        protected override void OnFormClosed(System.Windows.Forms.FormClosedEventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            // Cleanup Hotkeys and Firewall
            Program.UnregisterHotKey(this.Handle, Program.HOTKEY_ID_F9);
            Program.UnregisterHotKey(this.Handle, Program.HOTKEY_ID_F12);
            Program.RemoveFirewallRule();

            base.OnFormClosed(e);
        }
    }
}
