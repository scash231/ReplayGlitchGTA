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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("This application requires administrator privileges! Please run it as Administrator.");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            ShowInfoPopup();


            Console.WriteLine("==============================================");
            Console.WriteLine("         GTA Firewall Toggle Started          ");
            Console.WriteLine("==============================================");
            Console.WriteLine("Press Ctrl + F9  to block IP   (NO SAVING MODE ON)");
            Console.WriteLine("Press Ctrl + F12 to unblock IP (NO SAVING MODE OFF)");
            Console.WriteLine("Press Ctrl + C in this console to exit.");
            Console.WriteLine("==============================================\n");

            if (!RegisterHotKey(IntPtr.Zero, HOTKEY_ID_F9, MOD_CONTROL, VK_F9))
            {
                Console.WriteLine("Error registering Ctrl+F9 hotkey.");
            }

            if (!RegisterHotKey(IntPtr.Zero, HOTKEY_ID_F12, MOD_CONTROL, VK_F12))
            {
                Console.WriteLine("Error registering Ctrl+F12 hotkey.");
            }

            AppDomain.CurrentDomain.ProcessExit += (s, e) => 
            {
                UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_F9);
                UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_F12);
                RunNetshCommand("advfirewall firewall delete rule name=\"123456\"");
                Console.WriteLine("Cleaned up firewall rules on exit.");
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
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] NO SAVING MODE ON");
                        Console.ResetColor();
                        RunNetshCommand("advfirewall firewall add rule name=\"123456\" dir=out action=block remoteip=\"192.81.241.171\"");
                    }
                    else if (id == HOTKEY_ID_F12)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] NO SAVING MODE OFF");
                        Console.ResetColor();
                        RunNetshCommand("advfirewall firewall delete rule name=\"123456\"");
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

        static void RunNetshCommand(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running netsh: {ex.Message}");
            }
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
                Text = "How it works:\n\n1. Press Ctrl + F9 to block the IP (NO SAVING MODE ON).\n2. Press Ctrl + F12 to unblock the IP (NO SAVING MODE OFF).\n3. Keep the console window open to keep the hotkeys active.",
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
