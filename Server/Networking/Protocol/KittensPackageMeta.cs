namespace Server.Networking.Protocol;

public static class KittensPackageMeta
{
    public const byte StartByte = 0x02;
    public const byte EndByte = 0x03;
    public const int MaxPayloadSize = 4096; // <-- Увеличено
    public const int CommandByteIndex = 1;
    // Изменим структуру заголовка: START CMD LEN(2bytes) ... END
    public const int LengthByteIndex = 2; // Индекс первого байта длины
    public const int LengthSize = 2; // Размер поля длины в байтах (ushort)
    public const int PayloadStartIndex = LengthByteIndex + LengthSize; // 4
}