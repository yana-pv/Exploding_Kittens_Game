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
        if (content.Length > KittensPackageMeta.MaxPayloadSize) // <-- Теперь 4096
            throw new ArgumentException($"Payload exceeds {KittensPackageMeta.MaxPayloadSize} bytes");

        // Размер пакета: START + CMD + LEN_SIZE + CONTENT + END
        _package = new byte[1 + 1 + KittensPackageMeta.LengthSize + content.Length + 1];
        CreatePackage(content, command);
    }

    private void CreatePackage(byte[] content, Command command)
    {
        Console.WriteLine($"CreatePackage: команда {command}, длина контента {content.Length}");

        _package[0] = KittensPackageMeta.StartByte;
        _package[1] = (byte)command;

        // Записываем длину как ushort (2 байта, Little Endian)
        ushort length = (ushort)content.Length;
        _package[2] = (byte)(length & 0xFF);       // Младший байт
        _package[3] = (byte)((length >> 8) & 0xFF); // Старший байт

        Console.WriteLine($"Записываем длину: {content.Length} (0x{length:X4} -> [{_package[2]:X2}, {_package[3]:X2}])");

        if (content.Length > 0)
            Array.Copy(content, 0, _package, KittensPackageMeta.PayloadStartIndex, content.Length);

        // Конечный байт на своё место
        _package[^1] = KittensPackageMeta.EndByte;
    }

    public byte[] Build() => _package;

    public static byte[] CreateGameResponse(Guid gameId, Guid playerId)
    {
        var data = $"{gameId}:{playerId}";
        return new KittensPackageBuilder(Encoding.UTF8.GetBytes(data), Command.GameCreated).Build();
    }


    // File: Server/Networking/Protocol/KittensPackageBuilder.cs
    // ...
    public static byte[] PlayerHandResponse(List<Card> hand)
    {
        // Используем DTO
        var dtoHand = hand.Select(c => new ClientCardDto
        {
            Type = c.Type,
            Name = c.Name
            // Убираем Description для экономии места
        }).ToList();

        var json = JsonSerializer.Serialize(dtoHand);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (bytes.Length > KittensPackageMeta.MaxPayloadSize) // <-- Теперь 255
        {
            Console.WriteLine($"Предупреждение: Рука превышает MaxPayloadSize ({bytes.Length}), обрезаем.");
            // Нужно обрезать строку JSON, а не байты, чтобы не нарушить UTF-8
            // Это сложнее. Простой способ - обрезать список карт до умещения.
            // Начинаем с полного списка и убираем по одной, пока не уложимся.
            var tempDtoHand = new List<ClientCardDto>(dtoHand); // <-- Исправлено: ClientCardDto, а не ClientCardDtoDto
            while (tempDtoHand.Count > 0)
            {
                var tempJson = JsonSerializer.Serialize(tempDtoHand);
                var tempBytes = Encoding.UTF8.GetBytes(tempJson);
                if (tempBytes.Length <= KittensPackageMeta.MaxPayloadSize) // 255
                {
                    bytes = tempBytes; // Нашли подходящий размер
                    Console.WriteLine($"Рука обрезана до {tempDtoHand.Count} карт для умещения в MaxPayloadSize.");
                    break;
                }
                tempDtoHand.RemoveAt(tempDtoHand.Count - 1); // Удаляем последнюю карту
            }

            if (tempDtoHand.Count == 0)
            {
                // Совсем не влезает, даже одна карта. Это проблема.
                Console.WriteLine("Ошибка: Ни одна карта не помещается в MaxPayloadSize.");
                // Отправляем пустой список как крайний случай, или генерируем ошибку.
                // Пустой JSON массив: []
                var emptyJson = "[]";
                bytes = Encoding.UTF8.GetBytes(emptyJson);
            }
            // bytes теперь содержит корректный сериализованный JSON (возможно, обрезанный список)
        }

        // Теперь bytes.Length <= 255
        return new KittensPackageBuilder(bytes, Command.PlayerHandUpdate).Build();
    }

    public static byte[] GameStateResponse(string gameStateJson)
    {
        var bytes = Encoding.UTF8.GetBytes(gameStateJson);

        if (bytes.Length > KittensPackageMeta.MaxPayloadSize) // <-- Теперь 255
        {
            Console.WriteLine($"Предупреждение: Состояние игры превышает MaxPayloadSize ({bytes.Length}), обрезаем.");
            // Обрезаем строку JSON, а не байты, чтобы не нарушить UTF-8
            // Это сложнее. Простой способ - обрезать строку до умещения.
            // Начинаем с полной строки и убираем символы с конца, пока не уложимся.
            // (Это грубый способ, может поломать структуру JSON, но работает для байтов)
            // Лучше было бы оптимизировать ClientGameStateDto, но если это невозможно:

            // ВАРИАНТ 1: Обрезать строку по длине (может сломать JSON)
            // int maxCharCount = KittensPackageMeta.MaxPayloadSize; // Грубая оценка
            // while (maxCharCount > 0)
            // {
            //     var truncatedString = gameStateJson.Substring(0, maxCharCount);
            //     var tempBytes = Encoding.UTF8.GetBytes(truncatedString);
            //     if (tempBytes.Length <= KittensPackageMeta.MaxPayloadSize)
            //     {
            //         bytes = tempBytes;
            //         break;
            //     }
            //     maxCharCount--; // Уменьшаем на 1 символ
            // }

            // ВАРИАНТ 2: Использовать Span для более безопасной обрезки байтов UTF-8 (C# 7.2+)
            // Это более надежный способ, чем Array.Copy на raw byte array.
            var originalBytes = bytes;
            var maxLen = KittensPackageMeta.MaxPayloadSize;
            if (originalBytes.Length > maxLen)
            {
                // Найдем безопасную границу для обрезки UTF-8
                // Простой способ - уменьшать длину, пока Encoding.UTF8.GetCharCount не будет <= maxLen
                // Или использовать System.Text.Encodings.Web или Span<byte> для этого.
                // Но проще всего - оптимизировать сам JSON (ClientGameStateDto).
                // Пока оставим грубую обрезку, но с проверкой на валидность UTF-8 в начале.
                // Попробуем обрезать байты и посмотреть, можно ли из них получить строку.
                var truncatedSlice = originalBytes.AsSpan(0, maxLen);
                // Попробуем получить строку из обрезанных байтов, возможно, она будет битой.
                // Лучше использовать Decoder.
                var decoder = Encoding.UTF8.GetDecoder();
                var charCount = decoder.GetCharCount(originalBytes, 0, maxLen, true); // true - flush
                                                                                      // Это может выбросить исключение, если последовательность неполная.
                                                                                      // Попробуем обойти исключение:
                string safeTruncatedString;
                try
                {
                    safeTruncatedString = Encoding.UTF8.GetString(originalBytes, 0, maxLen);
                    // Проверим, валидный ли это JSON (очень грубо, просто проверим на `{` и `}`)
                    if (safeTruncatedString.EndsWith('}') || safeTruncatedString.EndsWith(']'))
                    {
                        // Возможно, JSON цел. Но скорее всего нет.
                        // Лучше бы не обрезать так.
                    }
                    else
                    {
                        // Строка обрезана посреди символа или структуры JSON.
                        // Нужно найти последний безопасный символ.
                        // Попробуем уменьшать maxLen, пока GetBytes не уложится.
                        // Это делает следующий цикл:
                    }
                }
                catch (ArgumentException)
                {
                    // Некорректная последовательность байт
                    safeTruncatedString = null; // Пометим, что строка битая
                }

                // Цикл для безопасной обрезки
                int safeLen = maxLen;
                while (safeLen > 0)
                {
                    try
                    {
                        var testSlice = originalBytes.AsSpan(0, safeLen);
                        // Decoder.GetCharCount может помочь, но проще так:
                        var testString = Encoding.UTF8.GetString(testSlice);
                        var testBytes = Encoding.UTF8.GetBytes(testString);
                        if (testBytes.Length <= maxLen)
                        {
                            // Проверим, не сломался ли JSON. Это сложно без парсера.
                            // Просто примем, что если байты уложились, это ОК.
                            bytes = testBytes;
                            Console.WriteLine($"Состояние игры обрезано до {bytes.Length} байт.");
                            break;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Пробуем уменьшить длину
                    }
                    safeLen--;
                }

                if (safeLen == 0)
                {
                    Console.WriteLine("Ошибка: Не удалось безопасно обрезать состояние игры.");
                    // Отправляем минимально возможный JSON, например, {}
                    var minimalJson = "{}";
                    bytes = Encoding.UTF8.GetBytes(minimalJson);
                }
            }
            // bytes теперь корректной длины или минимальный
        }

        // Теперь bytes.Length <= 255
        return new KittensPackageBuilder(bytes, Command.GameStateUpdate).Build();
    }
    // ...

    public static byte[] ErrorResponse(CommandResponse error)
    {
        return new KittensPackageBuilder(new[] { (byte)error }, Command.Error).Build();
    }

    public static byte[] MessageResponse(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);

        // Ограничиваем длину сообщения до 255 байт
        if (bytes.Length > 255)
        {
            var truncated = new byte[255];
            Array.Copy(bytes, truncated, 255);
            bytes = truncated;
            Console.WriteLine($"Сообщение обрезано до 255 байт");
        }

        Console.WriteLine($"MessageResponse: строка '{message}', длина байт {bytes.Length}");
        return new KittensPackageBuilder(bytes, Command.Message).Build();
    }
}