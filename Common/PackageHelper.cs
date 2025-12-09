// Common/PackageHelper.cs
namespace Common
{
    public static class PackageHelper
    {
        public const int MaxPacketSize = 4096;

        public static byte[] CreateGamePacket(string command, object data)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

            var packet = new Packet
            {
                Command = command,
                Data = jsonBytes
            };

            return packet.ToBytes();
        }

        public static (string Command, byte[] Data) ParseGamePacket(byte[] packetBytes)
        {
            var packet = Packet.FromBytes(packetBytes);
            return (packet.Command, packet.Data);
        }

        public static T DeserializeData<T>(byte[] data)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }
    }
}