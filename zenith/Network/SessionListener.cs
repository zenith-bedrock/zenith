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

    void SendPacket(RakNetSession session, params DataPacket[] packets)
    {
        SendPacket(session, RakNetSession.Priority.Normal, NONE, packets);
    }

    void SendPacket(RakNetSession session, RakNetSession.Priority priority, byte compression, params DataPacket[] packets)
    {
        var buffers = new List<byte[]>();
        foreach (var packet in packets) buffers.Add(packet.Encode().ToArray());
        var gamePacket = new GamePacket
        {
            Compression = compression,
            Buffers = buffers
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

                SendPacket(session, RakNetSession.Priority.Normal, NOT_PRESENT, settings);
                return;
            case (int)ProtocolInfo.LOGIN_PACKET:
                var loginPacket = DataPacket.From<LoginPacket>(stream);
                Console.WriteLine($"LoginPacket: {loginPacket.Protocol}");

                var playStatus = new PlayStatusPacket
                {
                    Status = 0
                };

                var resourcePacksInfo = new ResourcePacksInfoPacket
                {
                    MustAccept = true,
                    HasAddons = false,
                    HasScripts = false,
                    WorldTemplateVersion = ""
                };

                SendPacket(session, playStatus, resourcePacksInfo);
                return;
            case (int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET:
                var response = DataPacket.From<ResourcePackClientResponsePacket>(stream);
                Console.WriteLine($"ResourcePackClientResponsePacket: {response.Status}");

                switch (response.Status)
                {
                    case ResourcePackClientResponsePacket.STATUS_HAVE_ALL_PACKS:
                        var resourcePackStack = new ResourcePackStackPacket
                        {
                            MustAccept = false,
                            GameVersion = "1.21.51",
                            ExperimentsPreviouslyToggled = false,
                            HasEditorPacks = false
                        };

                        SendPacket(session, resourcePackStack);
                        return;
                    case ResourcePackClientResponsePacket.STATUS_COMPLETED:
                        var startGame = new StartGamePacket
                        {
                            LevelName = "world"
                        };

                        var playStatusSpawn = new PlayStatusPacket
                        {
                            Status = 3
                        };

                        SendPacket(session, startGame, playStatusSpawn);
                        return;
                }

                return;
        }

        Console.WriteLine($"Unhandled Data Packet: {header.Id}");
        stream.Dispose();
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