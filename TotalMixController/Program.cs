using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace TotalMixController
{
    /// <summary>
    /// OSC (Open Sound Control) client for sending messages via UDP
    /// </summary>
    public class OscClient : IDisposable
    {
        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _remoteEndpoint;

        public OscClient(string ipAddress, int port)
        {
            _remoteEndpoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
            _udpClient = new UdpClient();
        }

        /// <summary>
        /// Send an OSC message with a float value
        /// </summary>
        public void Send(string address, float value)
        {
            byte[] packet = BuildOscMessage(address, value);
            _udpClient.Send(packet, packet.Length, _remoteEndpoint);
        }

        /// <summary>
        /// Build an OSC message packet
        /// OSC format: address (null-terminated, padded to 4 bytes) + ",f" type tag + float value (big-endian)
        /// </summary>
        private byte[] BuildOscMessage(string address, float value)
        {
            // Calculate padded address length (must be multiple of 4)
            int addressLen = address.Length + 1; // +1 for null terminator
            int paddedAddressLen = (addressLen + 3) & ~3; // Round up to multiple of 4

            // Type tag ",f\0\0" = 4 bytes
            // Float = 4 bytes
            byte[] packet = new byte[paddedAddressLen + 4 + 4];

            // Write address
            byte[] addressBytes = Encoding.ASCII.GetBytes(address);
            Array.Copy(addressBytes, 0, packet, 0, addressBytes.Length);
            // Null terminator and padding are already 0

            // Write type tag ",f" at position paddedAddressLen
            int typeTagPos = paddedAddressLen;
            packet[typeTagPos] = (byte)',';
            packet[typeTagPos + 1] = (byte)'f';
            // Remaining bytes are 0 (null padding)

            // Write float value in big-endian format
            byte[] floatBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(floatBytes);
            }
            Array.Copy(floatBytes, 0, packet, typeTagPos + 4, 4);

            return packet;
        }

        public void Dispose()
        {
            _udpClient?.Dispose();
        }
    }

    /// <summary>
    /// TotalMix volume controller using OSC protocol
    /// </summary>
    public class TotalMixController : IDisposable
    {
        private readonly OscClient _oscClient;
        private readonly int[] _faders;
        private float _currentVolume = 0.5f;
        private bool _isMuted = false;
        private readonly float _step;
        private readonly float _unityGain = 0.7197f;

        // OSC addresses
        private const string BUS_OUTPUT = "/1/busOutput";
        private const string MASTER_VOLUME = "/1/mastervolume";
        private const string MAIN_MUTE = "/1/mainMute";

        public TotalMixController(string ipAddress, int port = 7001, float step = 0.02f, int[]? faders = null)
        {
            _oscClient = new OscClient(ipAddress, port);
            _step = step;
            _faders = faders ?? new int[] { 1, 2, 3, 4, 5, 6 };

            Console.WriteLine($"→ Sending OSC to {ipAddress}:{port}");
            Console.WriteLine($"→ Controlling faders: [{string.Join(", ", _faders)}]");

            // Select output bus on startup
            SelectOutputBus();
        }

        private void SelectOutputBus()
        {
            _oscClient.Send(BUS_OUTPUT, 1.0f);
            Console.WriteLine("→ Sent bus selection command");
        }

        public void SetVolume(float value)
        {
            value = Math.Clamp(value, 0.0f, 1.0f);
            _currentVolume = value;

            // Select output bus first
            _oscClient.Send(BUS_OUTPUT, 1.0f);

            // Send to all faders
            foreach (int fader in _faders)
            {
                string address = $"/1/volume{fader}";
                _oscClient.Send(address, value);
            }

            // Also send to master volume
            _oscClient.Send(MASTER_VOLUME, value);

            // Display volume bar
            int barLength = (int)(value * 30);
            string bar = new string('█', barLength) + new string('░', 30 - barLength);
            float db = 20 * (value - _unityGain) / _unityGain * 3;
            string dbStr = value <= 0 ? "-∞ dB" : $"{db:+0.0} dB";
            Console.Write($"\r🔊 Volume: [{bar}] {value:P0} (~{dbStr})  ");
        }

        public void VolumeUp()
        {
            SetVolume(_currentVolume + _step);
        }

        public void VolumeDown()
        {
            SetVolume(_currentVolume - _step);
        }

        public void ToggleMute()
        {
            _isMuted = !_isMuted;
            _oscClient.Send(MAIN_MUTE, _isMuted ? 1.0f : 0.0f);
            string status = _isMuted ? "🔇 MUTED" : "🔊 UNMUTED";
            Console.Write($"\r{status}                                          ");
        }

        public void SetUnityGain()
        {
            SetVolume(_unityGain);
            Console.Write(" [0dB]");
        }

        public float CurrentVolume => _currentVolume;

        public void Dispose()
        {
            _oscClient?.Dispose();
        }
    }

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
        private const uint VK_0 = 0x30;
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

            using var controller = new TotalMixController(ipAddress, port, step, faders);

            // Register hotkeys
            uint mods = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
            RegisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_UP, mods, VK_UP);
            RegisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_DOWN, mods, VK_DOWN);
            RegisterHotKey(IntPtr.Zero, HOTKEY_MUTE, mods, VK_M);
            RegisterHotKey(IntPtr.Zero, HOTKEY_UNITY, mods, VK_0);
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
║    Ctrl + Shift + 0      →  Set to 0dB (unity gain)          ║
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
