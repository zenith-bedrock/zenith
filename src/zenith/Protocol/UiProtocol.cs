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
}
