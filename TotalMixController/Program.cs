using System;
using System.Runtime.InteropServices;
using TotalMixLib;

namespace TotalMixConsole
{
    class Program
    {
        // Import Windows API for global hotkeys
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        // Hotkey IDs
        private const int HOTKEY_VOLUME_UP = 1;
        private const int HOTKEY_VOLUME_DOWN = 2;
        private const int HOTKEY_MUTE = 3;
        private const int HOTKEY_UNITY = 4;
        private const int HOTKEY_QUIT = 5;

        // Modifiers
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        // Virtual key codes
        private const uint VK_UP = 0x26;
        private const uint VK_DOWN = 0x28;
        private const uint VK_M = 0x4D;
        private const uint VK_R = 0x52;
        private const uint VK_Q = 0x51;

        private const int WM_HOTKEY = 0x0312;

        private static bool _running = true;

        static void Main(string[] args)
        {
            string ipAddress = "";
            int port = 7001;
            float step = 0.02f;
            int[] faders = { 1, 2, 3, 4, 5, 6 };

            // Parse command line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--ip":
                    case "-i":
                        if (i + 1 < args.Length) ipAddress = args[++i];
                        break;
                    case "--port":
                    case "-p":
                        if (i + 1 < args.Length) port = int.Parse(args[++i]);
                        break;
                    case "--step":
                    case "-s":
                        if (i + 1 < args.Length) step = float.Parse(args[++i]);
                        break;
                    case "--faders":
                    case "-f":
                        if (i + 1 < args.Length)
                        {
                            faders = Array.ConvertAll(args[++i].Split(','), int.Parse);
                        }
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return;
                }
            }

            if (string.IsNullOrEmpty(ipAddress))
            {
                Console.WriteLine("Error: --ip is required");
                PrintHelp();
                return;
            }

            PrintBanner();
            Console.WriteLine($"Connecting to TotalMix at {ipAddress}:{port}...\n");
            Console.WriteLine($"→ Controlling faders: [{string.Join(", ", faders)}]");

            using var controller = new TotalMixController(ipAddress, port, 9001, step, faders);

            // Subscribe to events
            controller.ConnectionVerified += (s, e) => 
                Console.WriteLine("\n✓ Received feedback from TotalMix - connection verified!");
            
            controller.VolumeChanged += (s, volume) =>
            {
                int barLength = (int)(volume * 30);
                string bar = new string('█', barLength) + new string('░', 30 - barLength);
                float db = TotalMixController.VolumeToDb(volume);
                string dbStr = float.IsNegativeInfinity(db) ? "-∞ dB" : $"{db:+0.0} dB";
                Console.Write($"\r🔊 Volume: [{bar}] {volume:P0} (~{dbStr})  ");
            };

            controller.MuteChanged += (s, muted) =>
            {
                string status = muted ? "🔇 MUTED" : "🔊 UNMUTED";
                Console.Write($"\r{status}                                          ");
            };

            // Sync volume from TotalMix
            Console.WriteLine("→ Requesting current volume from TotalMix...");
            if (controller.SyncVolume())
            {
                Console.WriteLine($"✓ Synced with TotalMix - current volume: {controller.CurrentVolume:P0}");
            }
            else
            {
                Console.WriteLine("⚠ No feedback received - starting at 0dB (unity gain)");
                Console.WriteLine("  Tip: Check TotalMix OSC 'IP or Host Name' is set to YOUR computer's IP");
            }

            // Register hotkeys
            uint mods = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
            RegisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_UP, mods, VK_UP);
            RegisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_DOWN, mods, VK_DOWN);
            RegisterHotKey(IntPtr.Zero, HOTKEY_MUTE, mods, VK_M);
            RegisterHotKey(IntPtr.Zero, HOTKEY_UNITY, mods, VK_R);
            RegisterHotKey(IntPtr.Zero, HOTKEY_QUIT, mods, VK_Q);

            Console.WriteLine("\n✓ Hotkeys registered. Press Ctrl+Shift+Q to quit.\n");
            Console.WriteLine("Waiting for input...\n");

            // Message loop
            while (_running && GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_HOTKEY)
                {
                    int id = (int)msg.wParam;
                    switch (id)
                    {
                        case HOTKEY_VOLUME_UP:
                            controller.VolumeUp();
                            break;
                        case HOTKEY_VOLUME_DOWN:
                            controller.VolumeDown();
                            break;
                        case HOTKEY_MUTE:
                            controller.ToggleMute();
                            break;
                        case HOTKEY_UNITY:
                            Console.WriteLine("\n→ Setting to 0dB unity gain...");
                            controller.SetUnityGain();
                            break;
                        case HOTKEY_QUIT:
                            _running = false;
                            break;
                    }
                }
            }

            // Cleanup
            UnregisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_UP);
            UnregisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_DOWN);
            UnregisterHotKey(IntPtr.Zero, HOTKEY_MUTE);
            UnregisterHotKey(IntPtr.Zero, HOTKEY_UNITY);
            UnregisterHotKey(IntPtr.Zero, HOTKEY_QUIT);

            Console.WriteLine("\n\nGoodbye!");
        }

        static void PrintBanner()
        {
            Console.WriteLine(@"
╔══════════════════════════════════════════════════════════════╗
║             TotalMix Remote Volume Controller                ║
╠══════════════════════════════════════════════════════════════╣
║  Hotkeys:                                                    ║
║    Ctrl + Shift + Up     →  Volume Up                        ║
║    Ctrl + Shift + Down   →  Volume Down                      ║
║    Ctrl + Shift + M      →  Mute / Unmute                    ║
║    Ctrl + Shift + R      →  Reset to 0dB (unity gain)        ║
║    Ctrl + Shift + Q      →  Quit                             ║
╚══════════════════════════════════════════════════════════════╝
");
        }

        static void PrintHelp()
        {
            Console.WriteLine(@"
TotalMix Volume Controller - Control TotalMix via OSC

Usage: TotalMixController.exe --ip <ADDRESS> [options]

Options:
  --ip, -i <ADDRESS>     IP address of TotalMix computer (required)
  --port, -p <PORT>      OSC port (default: 7001)
  --step, -s <STEP>      Volume step size (default: 0.02)
  --faders, -f <LIST>    Comma-separated fader numbers (default: 1,2,3,4,5,6)
  --help, -h             Show this help

Examples:
  TotalMixController.exe --ip 192.168.1.101
  TotalMixController.exe --ip 192.168.1.101 --faders 1,2,3,4
");
        }
    }
}
