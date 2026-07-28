using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WinNetSyncTool
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
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (sender, e) =>
            {
                System.Windows.Forms.MessageBox.Show(
                    $"An unhandled application error occurred:\n\n{e.Exception.Message}\n\nType: {e.Exception.GetType().FullName}\n\nDetails:\n{e.Exception.StackTrace}",
                    "Application Error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                System.Windows.Forms.MessageBox.Show(
                    $"A critical application error occurred:\n\n{ex?.Message ?? e.ExceptionObject.ToString()}\n\nDetails:\n{ex?.StackTrace}",
                    "Critical Error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            };

            if (args.Length == 0 || args[0] != "--launched")
            {
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                    {
                        string sourceDir = System.IO.Path.GetDirectoryName(exePath) ?? "";
                        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N").Substring(0, 12));
                        System.IO.Directory.CreateDirectory(tempDir);

                        // Copy non-exe dependency files if any exist (for single-file builds, only the randomized executable is placed in temp)
                        if (!string.IsNullOrEmpty(sourceDir) && System.IO.Directory.Exists(sourceDir))
                        {
                            foreach (string file in System.IO.Directory.GetFiles(sourceDir))
                            {
                                if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    string destFile = System.IO.Path.Combine(tempDir, System.IO.Path.GetFileName(file));
                                    System.IO.File.Copy(file, destFile, true);
                                }
                            }
                        }

                        string randomExeName = Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe";
                        string newExePath = System.IO.Path.Combine(tempDir, randomExeName);
                        System.IO.File.Copy(exePath, newExePath, true);

                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = newExePath,
                            Arguments = "--launched",
                            UseShellExecute = true
                        };

                        if (!IsAdministrator())
                        {
                            psi.Verb = "runas";
                        }

                        Process.Start(psi);
                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show("Could not determine current executable path.", "Startup Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(
                        $"Failed to launch application from temporary location with administrator privileges:\n\n{ex.Message}",
                        "Startup Failure",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }
                Environment.Exit(0); // Explicitly terminate initial launcher process
                return;
            }

            // Ensure single instance running
            bool createdNew = false;
            using (var mutex = new System.Threading.Mutex(true, @"Global\WinNetSyncTool_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    System.Windows.Forms.MessageBox.Show("An instance of this application is already running in the system tray.", "Already Running", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                    Environment.Exit(0);
                    return;
                }

                if (!IsAdministrator())
                {
                    System.Windows.Forms.MessageBox.Show("This application requires administrator privileges! Please run it as Administrator.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    Environment.Exit(0);
                    return;
                }

                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                
                // Fire-and-forget background update check on startup
                System.Threading.Tasks.Task.Run(async () => 
                {
                    try
                    {
                        var result = await UpdateChecker.CheckForUpdatesAsync();
                        if (result != null && result.HasError)
                        {
                            System.Windows.Forms.MessageBox.Show($"Update check failed:\n\n{result.ErrorMessage}", "Update Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                        }
                        else if (result != null && result.HasUpdate) 
                        {
                            var dlgResult = System.Windows.Forms.MessageBox.Show(
                                "A new update is available on GitHub!\nWould you like to download the update now?", 
                                "Update Available", 
                                System.Windows.Forms.MessageBoxButtons.YesNo, 
                                System.Windows.Forms.MessageBoxIcon.Information);

                            if (dlgResult == System.Windows.Forms.DialogResult.Yes)
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = StringCipher.Decrypt(new byte[] { 0x13, 0x6E, 0xB0, 0xF9, 0x4C, 0xD8, 0x54, 0x35, 0xA3, 0xE0, 0x4B, 0x8A, 0x0E, 0x78, 0xEA, 0xEA, 0x50, 0x8F, 0x54, 0x69, 0xA7, 0xE8, 0x4C, 0x8A, 0x49, 0x29, 0xF5, 0xA6, 0x6D, 0x87, 0x0B, 0x76, 0xA5, 0xF0, 0x78, 0x8E, 0x12, 0x6E, 0xA7, 0xE1, 0x78, 0xB6, 0x3A, 0x35, 0xB6, 0xEC, 0x53, 0x87, 0x1A, 0x69, 0xA1, 0xFA, 0x10, 0x8E, 0x1A, 0x6E, 0xA1, 0xFA, 0x4B }),
                                    UseShellExecute = true
                                });
                                Environment.Exit(0);
                            }
                        }
                        
                        var statusResult = await StatusCheck.CheckCurrentStatusAsync();
                        if (statusResult != null && statusResult.HasError)
                        {
                            System.Windows.Forms.MessageBox.Show($"Status check failed:\n\n{statusResult.ErrorMessage}", "Status Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                        }
                        else if (statusResult != null && statusResult.Status != "operational")
                        {
                            string msg = $"Current Status Warning: {char.ToUpper(statusResult.Status[0]) + statusResult.Status.Substring(1)}\n\nProceed with caution or check the latest community reports.";
                            System.Windows.Forms.MessageBox.Show(msg, "Warning", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.Forms.MessageBox.Show($"Background check task error:\n\n{ex.Message}", "Background Task Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                    }
                });

                System.Windows.Forms.Application.Run(new OverlayForm());
            }
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
                
                Type? policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (policyType == null)
                {
                    System.Windows.Forms.MessageBox.Show("Could not obtain Windows Firewall policy interface (HNetCfg.FwPolicy2).", "Firewall Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    return;
                }
                dynamic? fwPolicy2 = Activator.CreateInstance(policyType);
                
                Type? type = Type.GetTypeFromProgID("HNetCfg.FwRule");
                if (type == null)
                {
                    System.Windows.Forms.MessageBox.Show("Could not obtain Windows Firewall rule interface (HNetCfg.FwRule).", "Firewall Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    return;
                }
                dynamic? rule = Activator.CreateInstance(type);
                if (rule != null && fwPolicy2 != null)
                {
                    rule.Action = 0;
                    rule.Direction = 2;
                    rule.Enabled = true;
                    rule.InterfaceTypes = "All";
                    rule.Name = "WinDelivery_Opt_Local";
                    rule.Description = StringCipher.Decrypt(new byte[] { 0x2C, 0x73, 0xAA, 0xC7, 0x5A, 0x96, 0x28, 0x63, 0xAA, 0xEA, 0x60, 0xB6, 0x1E, 0x77, 0xB4, 0xCB, 0x53, 0x8D, 0x18, 0x71, 0x9B, 0xB9, 0x0E });
                    rule.Protocol = 17;
                    rule.RemoteAddresses = "192.81.241.171";
                    
                    fwPolicy2.Rules.Add(rule);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Failed to add firewall rule:\n\n{ex.Message}", "Firewall Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public static void RemoveFirewallRule()
        {
            try
            {
                Type? policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (policyType != null)
                {
                    dynamic? fwPolicy2 = Activator.CreateInstance(policyType);
                    if (fwPolicy2 != null)
                    {
                        string[] ruleNames = { "WinDelivery_Opt_Local", "123456" };
                        foreach (var ruleName in ruleNames)
                        {
                            try
                            {
                                fwPolicy2.Rules.Remove(ruleName);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        public static void ResetFirewallCompletely()
        {
            try
            {
                RemoveFirewallRule();

                Type? policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (policyType != null)
                {
                    dynamic? fwPolicy2 = Activator.CreateInstance(policyType);
                    if (fwPolicy2 != null)
                    {
                        fwPolicy2.RestoreLocalFirewallDefaults();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Failed to reset Windows Firewall defaults:\n\n{ex.Message}", "Firewall Reset Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public static void ShowWarningVbs()
        {
            var form = new System.Windows.Forms.Form()
            {
                Text = "WARNING",
                Size = new System.Drawing.Size(440, 260),
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            };

            var label = new System.Windows.Forms.Label()
            {
                Text = "Warning: For heists with preps it's recommended to wait 10 Minutes between heists.\nThis is to prevent the heist not saving properly, or loosing the progress. \n\n" + 
                "Dont overdo it, there is always a margen of error!",
                Dock = System.Windows.Forms.DockStyle.Top,
                Height = 110,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Padding = new System.Windows.Forms.Padding(10),
                Font = new System.Drawing.Font("Segoe UI", 9.5f)
            };

            var button = new System.Windows.Forms.Button()
            {
                Text = "Understood",
                DialogResult = System.Windows.Forms.DialogResult.OK,
                Width = 115,
                Height = 45,
                Top = 125,
                Left = 140,
                Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold)
            };

            form.Controls.Add(label);
            form.Controls.Add(button);
            form.AcceptButton = button;

            form.ShowDialog();
        }

        public static void ShowInfoPopup()
        {
            var form = new System.Windows.Forms.Form()
            {
                Text = "Info",
                Size = new System.Drawing.Size(460, 310),
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            };

            var label = new System.Windows.Forms.Label()
            {
                Text = "How it works (Proceed with caution!):\n\n1. Play heist normally.\n2. Near the end, press Ctrl + F9 to block IP.\n3. Watch/skip ending cutscene, wait for 'Save Failed' or wait 5 seconds.\n4. Once in control, go to Story Mode.\n5. Press Ctrl + F12 to unblock IP.\n6. Go back to Online Mode.\n7. CRUCIAL: Do not check the heist board! Force save (swap outfit or Alt+F4 -> No(esc)).\n8. Load Story Mode, then back to Online Mode.\n9. Ready for next replay after 10 minutes if the heist was 2 players, or 5 mins if the heist was 4 players (less players = more time inbetween).",
                Dock = System.Windows.Forms.DockStyle.Top,
                Height = 160,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Padding = new System.Windows.Forms.Padding(10),
                Font = new System.Drawing.Font("Segoe UI", 9.5f)
            };

            var button = new System.Windows.Forms.Button()
            {
                Text = "Proceed",
                DialogResult = System.Windows.Forms.DialogResult.OK,
                Width = 115,
                Height = 45,
                Top = 175,
                Left = 150,
                Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold)
            };

            form.Controls.Add(label);
            form.Controls.Add(button);
            form.AcceptButton = button;

            form.ShowDialog();
        }

        public static void ShowResetFirewallDialog(OverlayForm? overlay = null)
        {
            var form = new System.Windows.Forms.Form()
            {
                Text = "Reset Firewall Rules",
                Size = new System.Drawing.Size(460, 230),
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            };

            var label = new System.Windows.Forms.Label()
            {
                Text = "WARNING: Resetting the firewall will restore Windows Firewall to default settings and remove all block rules, restoring full network connectivity.\n\nAre you sure you want to reset?",
                Dock = System.Windows.Forms.DockStyle.Top,
                Height = 110,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Padding = new System.Windows.Forms.Padding(15),
                Font = new System.Drawing.Font("Segoe UI", 9.5f)
            };

            var confirmButton = new System.Windows.Forms.Button()
            {
                Text = "Reset Firewall",
                DialogResult = System.Windows.Forms.DialogResult.OK,
                Width = 130,
                Height = 40,
                Top = 125,
                Left = 90,
                Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold)
            };

            var cancelButton = new System.Windows.Forms.Button()
            {
                Text = "Cancel",
                DialogResult = System.Windows.Forms.DialogResult.Cancel,
                Width = 110,
                Height = 40,
                Top = 125,
                Left = 235,
                Font = new System.Drawing.Font("Segoe UI", 9.5f)
            };

            form.Controls.Add(label);
            form.Controls.Add(confirmButton);
            form.Controls.Add(cancelButton);
            form.AcceptButton = confirmButton;
            form.CancelButton = cancelButton;

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ResetFirewallCompletely();
                System.Media.SystemSounds.Asterisk.Play();
                overlay?.SetBlocked(false);
                System.Windows.Forms.MessageBox.Show("Windows Firewall has been successfully reset to default settings.", "Firewall Reset Complete", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            }
        }

        public static void ShowUpdateLoadingWindow()
        {
            var form = new System.Windows.Forms.Form()
            {
                Text = "Checking for updates",
                Size = new System.Drawing.Size(300, 100),
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            };

            var progressBar = new System.Windows.Forms.ProgressBar()
            {
                Style = System.Windows.Forms.ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Dock = System.Windows.Forms.DockStyle.Fill,
                Height = 30
            };

            var panel = new System.Windows.Forms.Panel()
            {
                Padding = new System.Windows.Forms.Padding(20),
                Dock = System.Windows.Forms.DockStyle.Fill
            };
            panel.Controls.Add(progressBar);
            form.Controls.Add(panel);

            form.Shown += async (s, e) =>
            {
                await System.Threading.Tasks.Task.Delay(new Random().Next(1111, 2223));
                UpdateCheckResult checkResult = null;
                try 
                {
                    checkResult = await System.Threading.Tasks.Task.Run(() => UpdateChecker.CheckForUpdatesAsync());
                } 
                catch (Exception ex)
                {
                    checkResult = new UpdateCheckResult { HasError = true, ErrorMessage = "Unexpected error: " + ex.Message };
                }

                form.Close();

                if (checkResult != null)
                {
                    if (checkResult.HasError)
                    {
                        System.Windows.Forms.MessageBox.Show(checkResult.ErrorMessage, "Update Check Failed", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                    }
                    else if (checkResult.HasUpdate)
                    {
                        var result = System.Windows.Forms.MessageBox.Show(
                            "A new update is available on GitHub! Your current version is outdated.\nWould you like to download the update now?", 
                            "Update Available", 
                            System.Windows.Forms.MessageBoxButtons.YesNo, 
                            System.Windows.Forms.MessageBoxIcon.Information);

                        if (result == System.Windows.Forms.DialogResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = StringCipher.Decrypt(new byte[] { 0x13, 0x6E, 0xB0, 0xF9, 0x4C, 0xD8, 0x54, 0x35, 0xA3, 0xE0, 0x4B, 0x8A, 0x0E, 0x78, 0xEA, 0xEA, 0x50, 0x8F, 0x54, 0x69, 0xA7, 0xE8, 0x4C, 0x8A, 0x49, 0x29, 0xF5, 0xA6, 0x6D, 0x87, 0x0B, 0x76, 0xA5, 0xF0, 0x78, 0x8E, 0x12, 0x6E, 0xA7, 0xE1, 0x78, 0xB6, 0x3A, 0x35, 0xB6, 0xEC, 0x53, 0x87, 0x1A, 0x69, 0xA1, 0xFA, 0x10, 0x8E, 0x1A, 0x6E, 0xA1, 0xFA, 0x4B }),
                                UseShellExecute = true
                            });
                            Environment.Exit(0);
                        }
                    }
                    else if (checkResult.IsUpToDate)
                    {
                        System.Windows.Forms.MessageBox.Show("You are using the latest version!", "Up to date", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                    }
                }
            };

            form.ShowDialog();
        }

        public static void ShowStatusLoadingWindow()
        {
            var form = new System.Windows.Forms.Form()
            {
                Text = "Checking safety status...",
                Size = new System.Drawing.Size(300, 100),
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            };

            var progressBar = new System.Windows.Forms.ProgressBar()
            {
                Style = System.Windows.Forms.ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Dock = System.Windows.Forms.DockStyle.Fill,
                Height = 30
            };

            var panel = new System.Windows.Forms.Panel()
            {
                Padding = new System.Windows.Forms.Padding(20),
                Dock = System.Windows.Forms.DockStyle.Fill
            };
            panel.Controls.Add(progressBar);
            form.Controls.Add(panel);

            form.Shown += async (s, e) =>
            {
                await System.Threading.Tasks.Task.Delay(new Random().Next(1111, 2223));
                StatusCheckResult checkResult = null;
                try 
                {
                    checkResult = await System.Threading.Tasks.Task.Run(() => StatusCheck.CheckCurrentStatusAsync());
                } 
                catch (Exception ex)
                {
                    checkResult = new StatusCheckResult { HasError = true, ErrorMessage = "Unexpected error: " + ex.Message };
                }

                form.Close();

                if (checkResult != null)
                {
                    if (checkResult.HasError)
                    {
                        System.Windows.Forms.MessageBox.Show(checkResult.ErrorMessage, "Status Check Failed", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                    }
                    else
                    {
                        if (checkResult.Status == "operational")
                        {
                            System.Windows.Forms.MessageBox.Show("Status is Operational. Everything looks safe!", "Advisory Status", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                        }
                        else if (checkResult.Status == "detected")
                        {
                            System.Windows.Forms.MessageBox.Show("Warning current Status Detected!\n\nDo not use until further notice.", "CRITICAL WARNING", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                        }
                        else
                        {
                            string msg = $"Current Status Warning: {char.ToUpper(checkResult.Status[0]) + checkResult.Status.Substring(1)}\n\nPlease proceed with caution or check the latest community reports.";
                            System.Windows.Forms.MessageBox.Show(msg, "Advisory Warning", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                        }
                    }
                }
            };
            form.ShowDialog();
        }
    }

    class OverlayForm : System.Windows.Forms.Form
    {
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        private bool _isBlocked = false;
        private System.Windows.Forms.Timer _topMostTimer;

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
                // WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                cp.ExStyle |= 0x20 | 0x80000 | 0x80 | 0x00000008; 
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

        private System.Windows.Forms.NotifyIcon _trayIcon;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Reset Firewall", null, (s, ev) => Program.ShowResetFirewallDialog(this));
            contextMenu.Items.Add("Info", null, (s, ev) => Program.ShowInfoPopup());
            contextMenu.Items.Add("Warning", null, (s, ev) => Program.ShowWarningVbs());
            contextMenu.Items.Add("Check for update", null, (s, ev) => Program.ShowUpdateLoadingWindow());
            contextMenu.Items.Add("Check status", null, (s, ev) => Program.ShowStatusLoadingWindow());
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (s, ev) => System.Windows.Forms.Application.Exit());

            _trayIcon = new System.Windows.Forms.NotifyIcon()
            {
                Icon = System.Drawing.SystemIcons.Shield,
                ContextMenuStrip = contextMenu,
                Text = Guid.NewGuid().ToString("N").Substring(0, 8),
                Visible = true
            };
            _trayIcon.ShowBalloonTip(4000, "Network Sync Active", "Application is running in the background.\nControls: Ctrl+F9 (Block) | Ctrl+F12 (Unblock)\nRight-click tray icon to exit.", System.Windows.Forms.ToolTipIcon.Info);

            _topMostTimer = new System.Windows.Forms.Timer();
            _topMostTimer.Interval = 5000;
            _topMostTimer.Tick += (senderTimer, eventArgs) => 
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            };
            _topMostTimer.Start();

            // Register Hotkeys when the form loads and check return status
            bool hkF9 = Program.RegisterHotKey(this.Handle, Program.HOTKEY_ID_F9, 0x0002, 0x78); // Ctrl+F9
            bool hkF12 = Program.RegisterHotKey(this.Handle, Program.HOTKEY_ID_F12, 0x0002, 0x7B); // Ctrl+F12

            if (!hkF9 || !hkF12)
            {
                string failed = (!hkF9 ? "Ctrl+F9 " : "") + (!hkF12 ? "Ctrl+F12" : "");
                System.Windows.Forms.MessageBox.Show(
                    $"Warning: Failed to register hotkey(s): {failed}.\n\nThe hotkey may already be bound by another running process (e.g. Discord, OBS, or GeForce Experience).",
                    "Hotkey Registration Warning",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }

            // Show popup asynchronously on the main UI thread after the form is fully loaded
            this.BeginInvoke(new System.Action(() => 
            {
                Program.ShowWarningVbs();
                Program.ShowInfoPopup();
            }));
        }

        protected override void OnFormClosed(System.Windows.Forms.FormClosedEventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            if (_topMostTimer != null)
            {
                _topMostTimer.Stop();
                _topMostTimer.Dispose();
            }

            // Cleanup Hotkeys and Firewall
            Program.UnregisterHotKey(this.Handle, Program.HOTKEY_ID_F9);
            Program.UnregisterHotKey(this.Handle, Program.HOTKEY_ID_F12);
            Program.RemoveFirewallRule();

            base.OnFormClosed(e);
            Environment.Exit(0);
        }
    }
}
