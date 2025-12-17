namespace Server.Networking.Protocol;

public static class KittensPackageMeta
{
    public const byte StartByte = 0x02;
    public const byte EndByte = 0x03;
    public const int MaxPayloadSize = 4096;
    public const int CommandByteIndex = 1;
    public const int LengthByteIndex = 2; 
    public const int LengthSize = 2; 
    public const int PayloadStartIndex = LengthByteIndex + LengthSize;
}