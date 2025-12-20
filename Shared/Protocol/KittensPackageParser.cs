
using Shared.Models;

namespace Shared.Protocol;

public static class KittensPackageParser
{
    public static (Command Command, byte[] Payload)? TryParse(ReadOnlySpan<byte> data, out CommandResponse error)
    {
        error = CommandResponse.Ok;

        // Требуется минимум: START + CMD + LEN(2) + END = 5 байт
        if (data.Length < 5)
        {
            error = CommandResponse.InvalidAction;
            return null;
        }

        if (data[0] != KittensPackageMeta.StartByte || data[^1] != KittensPackageMeta.EndByte)
        {
            error = CommandResponse.InvalidAction;
            return null;
        }

        var commandByte = data[KittensPackageMeta.CommandByteIndex];
        if (!Enum.IsDefined(typeof(Command), commandByte))
        {
            error = CommandResponse.InvalidAction;
            return null;
        }

        var command = (Command)commandByte;

        ushort length = (ushort)(data[KittensPackageMeta.LengthByteIndex] | (data[KittensPackageMeta.LengthByteIndex + 1] << 8));

        // Ожидаемая длина пакета: START + CMD + LEN_SIZE + PAYLOAD + END
        int expectedTotalLength = 1 + 1 + KittensPackageMeta.LengthSize + length + 1;

        if (expectedTotalLength != data.Length)
        {
            error = CommandResponse.InvalidAction;
            return null;
        }
       
        var payload = length > 0
            ? data.Slice(KittensPackageMeta.PayloadStartIndex, length).ToArray()
            : Array.Empty<byte>();

        return (command, payload);
    }
}