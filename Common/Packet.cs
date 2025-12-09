using System.Text;

namespace Common;

// Common/Packet.cs
public class Packet
{
    public string Command { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public byte[] ToBytes()
    {
        var commandBytes = Encoding.UTF8.GetBytes(Command);
        var length = commandBytes.Length;

        var packet = new byte[4 + length + Data.Length];

        // Длина команды (2 байта)
        packet[0] = (byte)(length >> 8);
        packet[1] = (byte)length;

        // Длина данных (2 байта)
        packet[2] = (byte)(Data.Length >> 8);
        packet[3] = (byte)Data.Length;

        // Команда
        Array.Copy(commandBytes, 0, packet, 4, length);

        // Данные
        Array.Copy(Data, 0, packet, 4 + length, Data.Length);

        return packet;
    }

    public static Packet FromBytes(byte[] data)
    {
        try
        {
            Console.WriteLine($"DEBUG_PACKET: FromBytes вызван с длиной {data.Length}");

            if (data.Length < 4)
            {
                Console.WriteLine($"DEBUG_PACKET: Ошибка: слишком короткий пакет ({data.Length} байт)");
                return new Packet { Command = "ERROR", Data = Array.Empty<byte>() };
            }

            int commandLength = (data[0] << 8) | data[1];
            int dataLength = (data[2] << 8) | data[3];

            Console.WriteLine($"DEBUG_PACKET: commandLength: {commandLength}, dataLength: {dataLength}");

            if (4 + commandLength > data.Length)
            {
                Console.WriteLine($"DEBUG_PACKET: Ошибка: неверная длина команды");
                return new Packet { Command = "ERROR", Data = Array.Empty<byte>() };
            }

            var command = Encoding.UTF8.GetString(data, 4, commandLength);
            Console.WriteLine($"DEBUG_PACKET: Команда: '{command}'");

            if (data.Length < 4 + commandLength + dataLength)
            {
                Console.WriteLine($"DEBUG_PACKET: Ошибка: неполный пакет данных");
                return new Packet { Command = command, Data = Array.Empty<byte>() };
            }

            var packetData = new byte[dataLength];
            if (dataLength > 0)
            {
                Array.Copy(data, 4 + commandLength, packetData, 0, dataLength);
            }

            Console.WriteLine($"DEBUG_PACKET: Успешно создан пакет '{command}' с {dataLength} байтами данных");

            return new Packet
            {
                Command = command,
                Data = packetData
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DEBUG_PACKET: Исключение в FromBytes: {ex.Message}");
            Console.WriteLine($"DEBUG_PACKET: StackTrace: {ex.StackTrace}");
            return new Packet { Command = "ERROR", Data = Array.Empty<byte>() };
        }
    }
}