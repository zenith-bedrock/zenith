using Zenith.Packets;
using Zenith.Session;

namespace Zenith.Protocol;

/// <summary>Transmite UI já decidida (toast / forms). Sem FormId allocator nem estado.</summary>
sealed class UiProtocol
{
    private readonly NetworkSession _session;

    public UiProtocol(NetworkSession session) => _session = session;

    public void SendToast(string title, string content) =>
        _session.SendDataPacket(new ToastRequestPacket
        {
            Title = title,
            Content = content
        });

    public void SendModalForm(uint formId, string formUiJson) =>
        _session.SendDataPacket(new ModalFormRequestPacket
        {
            FormId = formId,
            FormUiJson = formUiJson
        });

    public void SendServerSettings(uint formId, string formUiJson) =>
        _session.SendDataPacket(new ServerSettingsResponsePacket
        {
            FormId = formId,
            FormUiJson = formUiJson
        });

    public void SendCloseForms() =>
        _session.SendDataPacket(new ClientboundCloseFormPacket());

    public void SendTitle(string text, SetTitlePacket.TitleType titleType = SetTitlePacket.TitleType.Title,
        int fadeIn = 10, int stay = 70, int fadeOut = 20,
        string xuid = "", string platformId = "", string filteredText = "") =>
        _session.SendDataPacket(new SetTitlePacket
        {
            Type = titleType,
            TitleText = text,
            FadeInTime = fadeIn,
            StayTime = stay,
            FadeOutTime = fadeOut,
            Xuid = xuid,
            PlatformOnlineId = platformId,
            FilteredTitleMessage = filteredText
        });

    public void SendTitleTimes(int fadeIn, int stay, int fadeOut) =>
        _session.SendDataPacket(new SetTitlePacket
        {
            Type = SetTitlePacket.TitleType.Times,
            FadeInTime = fadeIn,
            StayTime = stay,
            FadeOutTime = fadeOut
        });

    public void SendClearTitle() =>
        _session.SendDataPacket(new SetTitlePacket
        {
            Type = SetTitlePacket.TitleType.Clear
        });

    public void SendResetTitle() =>
        _session.SendDataPacket(new SetTitlePacket
        {
            Type = SetTitlePacket.TitleType.Reset
        });
}
