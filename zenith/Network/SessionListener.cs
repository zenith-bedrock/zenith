using System.IO.Compression;
using zenith.Network.Protocol;
using Zenith.Raknet;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace zenith.Network;

class SessionListener : IRakNetSessionListener
{

    public const byte ZLIB = 0x00;
    public const byte SNAPPY = 0x01;
    public const byte NOT_PRESENT = 0x02;
    public const byte NONE = 0xff;

    void SendPacket(RakNetSession session, DataPacket packet, RakNetSession.Priority priority = RakNetSession.Priority.Normal)
    {
        var gamePacket = new GamePacket
        {
            Buffers = { packet.Encode().ToArray() }
        };

        var frame = new Frame
        {
            Reliability = Zenith.Raknet.Enumerator.Reliability.Reliable,
            OrderChannel = 0,
            Buffer = gamePacket.Encode().ToArray()
        };

        session.SendFrame(frame, priority);
    }

    public void HandleDataPacket(RakNetSession session, byte[] buffer)
    {
        var stream = new BinaryStream(buffer);

        var header = new DataPacket.HeaderInfo();
        header.Decode(stream);

        switch (header.Id)
        {
            case (int)ProtocolInfo.LOGIN_PACKET:
                var loginPacket = DataPacket.From<LoginPacket>(stream);
                Console.WriteLine($"LoginPacket: {loginPacket.Protocol}");
                break;
            case (int)ProtocolInfo.REQUEST_NETWORK_SETTINGS_PACKET:
                var requestNetworkSettings = DataPacket.From<RequestNetworkSettingsPacket>(stream);
                Console.WriteLine($"RequestNetworkSettingsPacket: {requestNetworkSettings.ProtocolVersion}");

                var settings = new NetworkSettingsPacket
                {
                    CompressionThreshold = 256,
                    CompressionAlgorithm = NONE,
                    EnableClientThrottling = false,
                    ClientThrottleThreshold = 0,
                    ClientThrottleScalar = 0
                };

                SendPacket(session, settings);
                return;
        }

        Console.WriteLine($"Unhandled Data Packet: {header.Id}");
    }

    public bool HandleGamePacket(RakNetSession session, BinaryStream stream)
    {
        var compressionType = stream.Buffer[0];

        switch (compressionType)
        {
            case ZLIB or SNAPPY or NONE:
                stream.ReadByte();
                break;
            default:
                compressionType = NOT_PRESENT;
                break;
        }

        if (compressionType == ZLIB)
        {
            var buffer = stream.ReadRemaining().ToArray();
            stream.Dispose();

            using (var memoryStream = new MemoryStream(buffer))
            using (var inflater = new DeflateStream(memoryStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                inflater.CopyTo(outputStream);
                buffer = outputStream.ToArray();
            }

            stream = new BinaryStream(buffer);
        }

        // TODO: snappy compression

        var gamePacket = IPacket.From<GamePacket>(stream);
        foreach (var buffer in gamePacket.Buffers)
        {
            HandleDataPacket(session, buffer);
        }

        stream.Dispose();
        return false;
    }
}