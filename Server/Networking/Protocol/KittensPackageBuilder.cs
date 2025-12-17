using Server.Game.Enums;
using Server.Game.Models;
using Server.Networking.Commands;
using System.Text;
using System.Text.Json;

namespace Server.Networking.Protocol;

public class KittensPackageBuilder
{
    private byte[] _package;

    public KittensPackageBuilder(byte[] content, Command command)
    {
        if (content.Length > KittensPackageMeta.MaxPayloadSize) 
            throw new ArgumentException($"Payload exceeds {KittensPackageMeta.MaxPayloadSize} bytes");

        _package = new byte[1 + 1 + KittensPackageMeta.LengthSize + content.Length + 1];
        CreatePackage(content, command);
    }

    private void CreatePackage(byte[] content, Command command)
    {
        _package[0] = KittensPackageMeta.StartByte;
        _package[1] = (byte)command;

        ushort length = (ushort)content.Length;
        _package[2] = (byte)(length & 0xFF);      
        _package[3] = (byte)((length >> 8) & 0xFF);

        if (content.Length > 0)
        {
            Array.Copy(content, 0, _package, KittensPackageMeta.PayloadStartIndex, content.Length);
        }

        _package[^1] = KittensPackageMeta.EndByte;
    }

    public byte[] Build() => _package;

    public static byte[] CreateGameResponse(Guid gameId, Guid playerId)
    {
        var data = $"{gameId}:{playerId}";
        return new KittensPackageBuilder(Encoding.UTF8.GetBytes(data), Command.GameCreated).Build();
    }

    public static byte[] PlayerHandResponse(List<Card> hand)
    {
        var dtoHand = hand.Select(c => new ClientCardDto
        {
            Type = c.Type,
            Name = c.Name
        }).ToList();

        var json = JsonSerializer.Serialize(dtoHand);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (bytes.Length > KittensPackageMeta.MaxPayloadSize) 
        {
            var tempDtoHand = new List<ClientCardDto>(dtoHand); 

            while (tempDtoHand.Count > 0)
            {
                var tempJson = JsonSerializer.Serialize(tempDtoHand);
                var tempBytes = Encoding.UTF8.GetBytes(tempJson);
                if (tempBytes.Length <= KittensPackageMeta.MaxPayloadSize)
                {
                    bytes = tempBytes; 
                    break;
                }
                tempDtoHand.RemoveAt(tempDtoHand.Count - 1); 
            }

            if (tempDtoHand.Count == 0)
            {
                var emptyJson = "[]";
                bytes = Encoding.UTF8.GetBytes(emptyJson);
            }
        }

        return new KittensPackageBuilder(bytes, Command.PlayerHandUpdate).Build();
    }

    public static byte[] GameStateResponse(string gameStateJson)
    {
        var bytes = Encoding.UTF8.GetBytes(gameStateJson);

        if (bytes.Length > KittensPackageMeta.MaxPayloadSize) 
        {
            var originalBytes = bytes;
            var maxLen = KittensPackageMeta.MaxPayloadSize;
            if (originalBytes.Length > maxLen)
            {
                var truncatedSlice = originalBytes.AsSpan(0, maxLen);

                var decoder = Encoding.UTF8.GetDecoder();
                var charCount = decoder.GetCharCount(originalBytes, 0, maxLen, true); 
                string safeTruncatedString;
                try
                {
                    safeTruncatedString = Encoding.UTF8.GetString(originalBytes, 0, maxLen);
                }
                catch (ArgumentException)
                {
                    safeTruncatedString = null; 
                }

                int safeLen = maxLen;
                while (safeLen > 0)
                {
                    var testSlice = originalBytes.AsSpan(0, safeLen);
                    var testString = Encoding.UTF8.GetString(testSlice);
                    var testBytes = Encoding.UTF8.GetBytes(testString);
                    if (testBytes.Length <= maxLen)
                    {
                        bytes = testBytes;
                        break;
                    }

                    safeLen--;
                }

                if (safeLen == 0)
                {
                    var minimalJson = "{}";
                    bytes = Encoding.UTF8.GetBytes(minimalJson);
                }
            }
        }

        return new KittensPackageBuilder(bytes, Command.GameStateUpdate).Build();
    }

    public static byte[] ErrorResponse(CommandResponse error)
    {
        return new KittensPackageBuilder(new[] { (byte)error }, Command.Error).Build();
    }

    public static byte[] MessageResponse(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);

        if (bytes.Length > 255)
        {
            var truncated = new byte[255];
            Array.Copy(bytes, truncated, 255);
            bytes = truncated;
        }

        return new KittensPackageBuilder(bytes, Command.Message).Build();
    }
}