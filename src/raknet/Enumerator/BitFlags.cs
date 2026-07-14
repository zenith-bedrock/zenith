namespace Zenith.Raknet.Enumerator;

[Flags]
public enum BitFlags : byte
{
    Valid = 0x80,
    Ack = 0x40,
    Nack = 0x20,
    Split = 0x10
}