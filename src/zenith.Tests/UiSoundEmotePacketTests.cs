using Xunit;
using Zenith.Packets;
using Zenith.Raknet.Stream;

namespace Zenith.Tests;

/// <summary>
/// Encode/Decode hygiene for collaborator UI/sound/emote DTOs (ADR §51).
/// Every introduced packet must round-trip with packet Id in Encode.
/// </summary>
public class UiSoundEmotePacketTests
{
    [Fact]
    public void ToastRequest_roundtrips_with_packet_id()
    {
        var original = new ToastRequestPacket { Title = "Hi", Content = "there" };
        var decoded = RoundTrip(original, ProtocolInfo.TOAST_REQUEST_PACKET, () => new ToastRequestPacket());
        Assert.Equal(original.Title, decoded.Title);
        Assert.Equal(original.Content, decoded.Content);
    }

    [Fact]
    public void PlaySound_uses_sound_pos_times_eight_and_le_floats()
    {
        var original = new PlaySoundPacket
        {
            SoundName = "random.click",
            PositionX = 1.5f,
            PositionY = 64f,
            PositionZ = -2.25f,
            Volume = 0.5f,
            Pitch = 1.25f,
            ServerSoundHandle = 0x1122334455667788UL
        };
        var decoded = RoundTrip(original, ProtocolInfo.PLAY_SOUND_PACKET, () => new PlaySoundPacket());
        Assert.Equal(original.SoundName, decoded.SoundName);
        Assert.Equal(original.PositionX, decoded.PositionX);
        Assert.Equal(original.PositionY, decoded.PositionY);
        Assert.Equal(original.PositionZ, decoded.PositionZ);
        Assert.Equal(original.Volume, decoded.Volume);
        Assert.Equal(original.Pitch, decoded.Pitch);
        Assert.Equal(original.ServerSoundHandle, decoded.ServerSoundHandle);
    }

    [Fact]
    public void PlaySound_roundtrips_without_optional_handle()
    {
        var original = new PlaySoundPacket
        {
            SoundName = "note.harp",
            PositionX = 0f,
            PositionY = 8f,
            PositionZ = 0f,
            Volume = 1f,
            Pitch = 1f,
            ServerSoundHandle = null
        };
        var decoded = RoundTrip(original, ProtocolInfo.PLAY_SOUND_PACKET, () => new PlaySoundPacket());
        Assert.Null(decoded.ServerSoundHandle);
        Assert.Equal("note.harp", decoded.SoundName);
    }

    [Fact]
    public void StopSound_roundtrips()
    {
        var original = new StopSoundPacket
        {
            SoundName = "music.game",
            StopAllSounds = true,
            StopMusic = true
        };
        var decoded = RoundTrip(original, ProtocolInfo.STOP_SOUND_PACKET, () => new StopSoundPacket());
        Assert.Equal(original.SoundName, decoded.SoundName);
        Assert.True(decoded.StopAllSounds);
        Assert.True(decoded.StopMusic);
    }

    [Fact]
    public void Emote_roundtrips_unsigned_runtime_and_byte_flags()
    {
        var original = new EmotePacket
        {
            ActorRuntimeId = 42,
            EmoteId = "emote.id",
            TickLength = 40,
            Xuid = "xuid",
            PlatformChatId = "pcid",
            Flags = EmotePacket.FlagServerSide | EmotePacket.FlagMuteChat
        };
        var decoded = RoundTrip(original, ProtocolInfo.EMOTE_PACKET, () => new EmotePacket());
        Assert.Equal(original.ActorRuntimeId, decoded.ActorRuntimeId);
        Assert.Equal(original.EmoteId, decoded.EmoteId);
        Assert.Equal(original.TickLength, decoded.TickLength);
        Assert.Equal(original.Xuid, decoded.Xuid);
        Assert.Equal(original.PlatformChatId, decoded.PlatformChatId);
        Assert.Equal(original.Flags, decoded.Flags);
    }

    [Fact]
    public void EmoteList_roundtrips_runtime_id_and_uuids()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var original = new EmoteListPacket
        {
            ActorRuntimeId = 7,
            Emotes = [a, b]
        };
        var decoded = RoundTrip(original, ProtocolInfo.EMOTE_LIST_PACKET, () => new EmoteListPacket());
        Assert.Equal(original.ActorRuntimeId, decoded.ActorRuntimeId);
        Assert.Equal(original.Emotes, decoded.Emotes);
    }

    [Fact]
    public void EmoteList_roundtrips_empty_list()
    {
        var original = new EmoteListPacket { ActorRuntimeId = 1, Emotes = [] };
        var decoded = RoundTrip(original, ProtocolInfo.EMOTE_LIST_PACKET, () => new EmoteListPacket());
        Assert.Equal(1UL, decoded.ActorRuntimeId);
        Assert.Empty(decoded.Emotes);
    }

    [Fact]
    public void EmoteList_decode_rejects_oversized_count()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt((int)ProtocolInfo.EMOTE_LIST_PACKET);
        writer.WriteUnsignedVarLong(1);
        writer.WriteUnsignedVarInt(EmoteListPacket.MaxEmotes + 1);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        _ = stream.ReadUnsignedVarInt();
        var packet = new EmoteListPacket();
        InvalidDataException? caught = null;
        try
        {
            packet.Decode(ref stream);
        }
        catch (InvalidDataException ex)
        {
            caught = ex;
        }
        finally
        {
            stream.Dispose();
        }

        Assert.NotNull(caught);
        Assert.Contains("exceeds", caught!.Message);
    }

    [Fact]
    public void ModalFormRequest_roundtrips()
    {
        var form = RoundTrip(
            new ModalFormRequestPacket { FormId = 9, FormUiJson = "{\"type\":\"modal\"}" },
            ProtocolInfo.MODAL_FORM_REQUEST_PACKET,
            () => new ModalFormRequestPacket());
        Assert.Equal(9u, form.FormId);
        Assert.Equal("{\"type\":\"modal\"}", form.FormUiJson);
    }

    [Fact]
    public void ModalFormResponse_cancel_reason_is_byte()
    {
        var original = new ModalFormResponsePacket
        {
            FormId = 3,
            FormUiJson = null,
            CancelReason = ModalFormResponsePacket.CancelUserClosed
        };
        var decoded = RoundTrip(original, ProtocolInfo.MODAL_FORM_RESPONSE_PACKET, () => new ModalFormResponsePacket());
        Assert.Equal(3u, decoded.FormId);
        Assert.Null(decoded.FormUiJson);
        Assert.Equal(ModalFormResponsePacket.CancelUserClosed, decoded.CancelReason);
    }

    [Fact]
    public void ModalFormResponse_roundtrips_submitted_json()
    {
        var original = new ModalFormResponsePacket
        {
            FormId = 5,
            FormUiJson = "true",
            CancelReason = null
        };
        var decoded = RoundTrip(original, ProtocolInfo.MODAL_FORM_RESPONSE_PACKET, () => new ModalFormResponsePacket());
        Assert.Equal(5u, decoded.FormId);
        Assert.Equal("true", decoded.FormUiJson);
        Assert.Null(decoded.CancelReason);
    }

    [Fact]
    public void ServerSettingsResponse_roundtrips()
    {
        var original = new ServerSettingsResponsePacket
        {
            FormId = 1,
            FormUiJson = "{\"type\":\"custom_form\"}"
        };
        var decoded = RoundTrip(original, ProtocolInfo.SERVER_SETTINGS_RESPONSE_PACKET, () => new ServerSettingsResponsePacket());
        Assert.Equal(original.FormId, decoded.FormId);
        Assert.Equal(original.FormUiJson, decoded.FormUiJson);
    }

    [Fact]
    public void ServerSettingsRequest_roundtrips_empty_body()
    {
        RoundTrip(
            new ServerSettingsRequestPacket(),
            ProtocolInfo.SERVER_SETTINGS_REQUEST_PACKET,
            () => new ServerSettingsRequestPacket());
    }

    [Fact]
    public void ClientboundCloseForm_roundtrips_id_only()
    {
        RoundTrip(
            new ClientboundCloseFormPacket(),
            ProtocolInfo.CLIENTBOUND_CLOSE_FORM_PACKET,
            () => new ClientboundCloseFormPacket());
    }

    private static T RoundTrip<T>(T original, ProtocolInfo expectedId, Func<T> factory)
        where T : DataPacket
    {
        var encoded = original.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        Assert.Equal((int)expectedId, (int)stream.ReadUnsignedVarInt());
        var decoded = factory();
        decoded.Decode(ref stream);
        stream.Dispose();
        return decoded;
    }
}
