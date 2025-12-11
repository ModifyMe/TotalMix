using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TotalMixLib
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
            int addressLen = address.Length + 1;
            int paddedAddressLen = (addressLen + 3) & ~3;

            byte[] packet = new byte[paddedAddressLen + 4 + 4];

            byte[] addressBytes = Encoding.ASCII.GetBytes(address);
            Array.Copy(addressBytes, 0, packet, 0, addressBytes.Length);

            int typeTagPos = paddedAddressLen;
            packet[typeTagPos] = (byte)',';
            packet[typeTagPos + 1] = (byte)'f';

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
}
