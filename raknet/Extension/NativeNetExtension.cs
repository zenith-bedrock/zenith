using System;
using System.Net;
using System.Numerics;

namespace Zenith.Raknet.Extension
{
    public static class NativeNetExtension
    {
        public static ulong ToUInt64(this IPEndPoint value)
        {
            var ipBytes = value.Address.GetAddressBytes();
            var port = (ushort)value.Port;

            if (ipBytes.Length != 4)
                throw new InvalidOperationException("Only IPv4 addresses are supported for UInt64 conversion.");

            ulong result = 0;
            result |= ((ulong)ipBytes[0]) << 40;
            result |= ((ulong)ipBytes[1]) << 32;
            result |= ((ulong)ipBytes[2]) << 24;
            result |= ((ulong)ipBytes[3]) << 16;
            result |= port;

            return result;
        }
        public static IPEndPoint ToIPEndPoint(this ulong value)
        {
            ushort port = (ushort)(value & 0xFFFF);

            byte[] ipBytes = new byte[4];
            ipBytes[0] = (byte)((value >> 40) & 0xFF);
            ipBytes[1] = (byte)((value >> 32) & 0xFF);
            ipBytes[2] = (byte)((value >> 24) & 0xFF);
            ipBytes[3] = (byte)((value >> 16) & 0xFF);

            IPAddress ipAddress = new(ipBytes);
            return new IPEndPoint(ipAddress, port);
        }

    }
}
