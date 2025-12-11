using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TotalMixLib
{
    /// <summary>
    /// TotalMix volume controller using OSC protocol.
    /// Controls RME TotalMix FX software via network.
    /// </summary>
    public class TotalMixController : IDisposable
    {
        private readonly OscClient _oscClient;
        private readonly int[] _faders;
        private float? _currentVolume = null;
        private bool _isMuted = false;
        private readonly float _step;
        private readonly float _unityGain = 0.7197f;
        private readonly int _receivePort;
        private UdpClient? _listener;
        private bool _receivedFeedback = false;
        private readonly string[] _volumeAddresses;

        // OSC addresses
        private const string BUS_OUTPUT = "/1/busOutput";
        private const string MASTER_VOLUME = "/1/mastervolume";
        private const string MAIN_MUTE = "/1/mainMute";

        /// <summary>
        /// Event raised when volume changes (from TotalMix feedback or local change)
        /// </summary>
        public event EventHandler<float>? VolumeChanged;

        /// <summary>
        /// Event raised when mute state changes
        /// </summary>
        public event EventHandler<bool>? MuteChanged;

        /// <summary>
        /// Event raised when feedback is received from TotalMix (connection verified)
        /// </summary>
        public event EventHandler? ConnectionVerified;

        /// <summary>
        /// Gets the current volume level (0.0 to 1.0)
        /// </summary>
        public float CurrentVolume => _currentVolume ?? _unityGain;

        /// <summary>
        /// Gets whether the output is currently muted
        /// </summary>
        public bool IsMuted => _isMuted;

        /// <summary>
        /// Gets whether feedback has been received from TotalMix
        /// </summary>
        public bool IsConnected => _receivedFeedback;

        /// <summary>
        /// Creates a new TotalMix controller
        /// </summary>
        /// <param name="ipAddress">IP address of the computer running TotalMix</param>
        /// <param name="sendPort">OSC port to send to (TotalMix 'Port incoming', default 7001)</param>
        /// <param name="receivePort">OSC port to receive on (TotalMix 'Port outgoing', default 9001)</param>
        /// <param name="step">Volume step size (default 0.02 = ~1dB)</param>
        /// <param name="faders">Array of fader numbers to control (default 1-6 for all analog outputs)</param>
        public TotalMixController(string ipAddress, int sendPort = 7001, int receivePort = 9001, 
                                   float step = 0.02f, int[]? faders = null)
        {
            _oscClient = new OscClient(ipAddress, sendPort);
            _step = step;
            _faders = faders ?? new int[] { 1, 2, 3, 4, 5, 6 };
            _receivePort = receivePort;
            _volumeAddresses = _faders.Select(f => $"/1/volume{f}").ToArray();

            StartListener();
            SelectOutputBus();
        }

        /// <summary>
        /// Requests current state from TotalMix and waits for response
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds to wait for response</param>
        /// <returns>True if volume was synced from TotalMix</returns>
        public bool SyncVolume(int timeoutMs = 600)
        {
            _oscClient.Send("/1/refresh", 1.0f);
            Thread.Sleep(timeoutMs / 2);

            if (_currentVolume == null)
            {
                _oscClient.Send(BUS_OUTPUT, 1.0f);
                Thread.Sleep(timeoutMs / 2);
            }

            if (_currentVolume == null)
            {
                _currentVolume = _unityGain;
                return false;
            }
            return true;
        }

        private void StartListener()
        {
            try
            {
                _listener = new UdpClient(_receivePort);
                Task.Run(() => ListenForFeedback());
            }
            catch (SocketException)
            {
                // Port in use or unavailable
            }
        }

        private void ListenForFeedback()
        {
            try
            {
                while (_listener != null)
                {
                    IPEndPoint? remoteEP = null;
                    byte[] data = _listener.Receive(ref remoteEP);
                    ProcessOscMessage(data);
                }
            }
            catch (SocketException)
            {
                // Socket closed
            }
        }

        private void ProcessOscMessage(byte[] data)
        {
            if (data.Length < 8) return;

            int nullPos = Array.IndexOf(data, (byte)0);
            if (nullPos < 0) return;

            string address = Encoding.ASCII.GetString(data, 0, nullPos);

            if (!_receivedFeedback)
            {
                _receivedFeedback = true;
                ConnectionVerified?.Invoke(this, EventArgs.Empty);
            }

            if (_volumeAddresses.Contains(address))
            {
                int paddedAddrLen = (nullPos + 4) & ~3;
                int floatPos = paddedAddrLen + 4;

                if (floatPos + 4 <= data.Length)
                {
                    byte[] floatBytes = new byte[4];
                    Array.Copy(data, floatPos, floatBytes, 0, 4);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(floatBytes);
                    }
                    float newVolume = BitConverter.ToSingle(floatBytes, 0);
                    _currentVolume = newVolume;
                    VolumeChanged?.Invoke(this, newVolume);
                }
            }
        }

        private void SelectOutputBus()
        {
            _oscClient.Send(BUS_OUTPUT, 1.0f);
        }

        /// <summary>
        /// Set volume to specific value
        /// </summary>
        /// <param name="value">Volume level from 0.0 (silent) to 1.0 (max)</param>
        public void SetVolume(float value)
        {
            value = Math.Clamp(value, 0.0f, 1.0f);
            _currentVolume = value;

            _oscClient.Send(BUS_OUTPUT, 1.0f);

            foreach (int fader in _faders)
            {
                string address = $"/1/volume{fader}";
                _oscClient.Send(address, value);
            }

            _oscClient.Send(MASTER_VOLUME, value);
            VolumeChanged?.Invoke(this, value);
        }

        /// <summary>
        /// Increase volume by one step
        /// </summary>
        public void VolumeUp()
        {
            SetVolume(CurrentVolume + _step);
        }

        /// <summary>
        /// Decrease volume by one step
        /// </summary>
        public void VolumeDown()
        {
            SetVolume(CurrentVolume - _step);
        }

        /// <summary>
        /// Toggle mute on/off for all controlled faders
        /// </summary>
        public void ToggleMute()
        {
            _isMuted = !_isMuted;
            float muteValue = _isMuted ? 1.0f : 0.0f;

            _oscClient.Send(BUS_OUTPUT, 1.0f);

            foreach (int fader in _faders)
            {
                string muteAddress = $"/1/mute/1/{fader}";
                _oscClient.Send(muteAddress, muteValue);
            }

            _oscClient.Send(MAIN_MUTE, muteValue);
            MuteChanged?.Invoke(this, _isMuted);
        }

        /// <summary>
        /// Set mute state
        /// </summary>
        public void SetMute(bool muted)
        {
            if (_isMuted != muted)
            {
                ToggleMute();
            }
        }

        /// <summary>
        /// Set volume to 0dB (unity gain) using TotalMix recall
        /// </summary>
        public void SetUnityGain()
        {
            _oscClient.Send("/1/mainRecall", 1.0f);
            SetVolume(_unityGain);
        }

        /// <summary>
        /// Convert volume value (0.0-1.0) to approximate dB
        /// </summary>
        public static float VolumeToDb(float volume)
        {
            if (volume <= 0) return float.NegativeInfinity;
            return 20 * (volume - 0.7197f) / 0.7197f * 3;
        }

        public void Dispose()
        {
            _listener?.Close();
            _listener?.Dispose();
            _oscClient?.Dispose();
        }
    }
}
