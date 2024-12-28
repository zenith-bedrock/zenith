using Zenith.Raknet.Stream;

namespace zenith.Network.Protocol;

class StartGamePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.START_GAME_PACKET;

    public string LevelName;
    public long EntityId = 0;
    public int GameMode = 0;
    public long Seed = 0;
    public short BiomeType = 0;
    public string BiomeName = "plains";
    public int Dimension = 0;
    public int Generator = 1;
    public int GameType = 0;
    public int Difficulty = 0;
    public int SpawnBlockX = 0;
    public int SpawnBlockY = 0;
    public int SpawnBlockZ = 0;
    public int EditorType = 0;
    public int StopTime = 0;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarLong(1);
        writer.WriteVarLong(1);
        writer.WriteVarInt(GameMode);
        writer.WriteFloat(0, BinaryStream.Endianess.Little); // x
        writer.WriteFloat(8, BinaryStream.Endianess.Little); // y
        writer.WriteFloat(0, BinaryStream.Endianess.Little); // z
        writer.WriteFloat(0, BinaryStream.Endianess.Little); // yaw
        writer.WriteFloat(0, BinaryStream.Endianess.Little); // pitch
        //Level settings
        writer.WriteLong(Seed);
        //Spawn settings
        writer.WriteShort(BiomeType, BinaryStream.Endianess.Little);
        writer.WriteVarString(BiomeName);
        writer.WriteVarInt(Dimension);
        //End of Spawn settings
        writer.WriteVarInt(Generator);
        writer.WriteVarInt(GameType);
        writer.WriteBool(false); //hardcore
        writer.WriteVarInt(Difficulty);
        writer.WriteVarInt(SpawnBlockX);
        writer.WriteVarInt(SpawnBlockY);
        writer.WriteVarInt(SpawnBlockZ);
        writer.WriteBool(false); //achievements
        writer.WriteBool(false);
        writer.WriteBool(false); //editorCreated
        writer.WriteBool(false); //editorExported
        writer.WriteVarInt(StopTime);
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteVarString("");
        writer.WriteFloat(0);
        writer.WriteFloat(0);
        writer.WriteBool(true); //platform content
        writer.WriteBool(true); //multiplayer?
        writer.WriteBool(true); //lan?
        writer.WriteVarInt(0); //xbox broadcast settings
        writer.WriteVarInt(0); //platform broadcast settings
        writer.WriteBool(true); //commands?
        writer.WriteBool(false); //texture packs?
        writer.WriteVarInt(0); //game rules
        writer.WriteInt(0); //experiments
        writer.WriteBool(false);
        writer.WriteBool(false); //bonus chest
        writer.WriteBool(false); //map
        writer.WriteByte(2); //permission level
        writer.WriteInt(0); //chunk tick range
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteVarString("1.21.51");
        writer.WriteInt(0);
        writer.WriteInt(0);
        writer.WriteBool(false);
        writer.WriteVarString("");
        writer.WriteVarString("");
        writer.WriteBool(false);
        writer.WriteByte(0);
        writer.WriteBool(false);
        //End of Level settings
        writer.WriteVarString("");
        writer.WriteVarString("");
        writer.WriteVarString("");
        writer.WriteVarString("");
        writer.WriteVarString(LevelName); //level name?
        writer.WriteVarString("");
        writer.WriteBool(false); //trial //ok
                                 //synced movement settings
        writer.WriteVarInt(0); //0 server auth off, need fix
        writer.WriteVarInt(80);
        writer.WriteBool(true);
        //end of synced movement settings
        writer.WriteLong(0);
        writer.WriteVarInt(0);
        writer.WriteVarInt(0); //block
        writer.WriteVarInt(0); //item
        writer.WriteVarString("");
        writer.WriteBool(true); //new inventory
        writer.WriteVarString("1.21.51");
        writer.WriteByte(0x0a); // nbt
        writer.WriteByte(0); // nbt
        writer.WriteByte(0); // nbt
        writer.WriteLong(0); //blockstate checksum
        writer.WriteLong(0); // uuid
        writer.WriteLong(0); // uuid
        writer.WriteBool(false);
        writer.WriteBool(true); //we use hashed block ids
        writer.WriteBool(true);
        return writer.GetBufferDisposing();
    }

    public override void Decode(BinaryStream stream) { }
}