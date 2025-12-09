using System.Net.Sockets;
using System.Text;
using Common; 


namespace Server
{
    public class Player
    {
        public Socket Socket { get; }
        public string Nickname { get; set; }
        public List<Card> Hand { get; set; }
        public bool IsAlive { get; set; }
        public bool HasDefuse { get; set; }
        public bool IsCurrentTurn { get; set; }

        // Для отслеживания состояния
        public bool DrewCardThisTurn { get; set; }
        public bool PlayedCardThisTurn { get; set; }
        public bool MustDrawCard { get; set; }
        public int AttackTurnsRemaining { get; set; }

        public Player(Socket socket)
        {
            Socket = socket;
            Nickname = $"Игрок_{Guid.NewGuid().ToString()[..8]}";
            Hand = new List<Card>();
            IsAlive = true;
            HasDefuse = false;
        }

        public void Send(byte[] data)
        {
            try
            {
                if (Socket.Connected)
                {
                    // Отправляем все байты
                    int totalSent = 0;
                    while (totalSent < data.Length)
                    {
                        int sent = Socket.Send(data, totalSent, data.Length - totalSent, SocketFlags.None);
                        if (sent == 0) break;
                        totalSent += sent;
                    }
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Ошибка отправки игроку {Nickname}: {ex.Message}");
            }
        }

        // В Player.cs - улучшаем SendPacket
        public void SendPacket(string command, byte[] data = null)
        {
            try
            {
                if (Socket.Connected)
                {
                    var packet = new Packet
                    {
                        Command = command,
                        Data = data ?? Array.Empty<byte>()
                    };

                    var bytes = packet.ToBytes();
                    Console.WriteLine($"DEBUG_SERVER: Подготовлен пакет '{command}' для {Nickname}, размер={bytes.Length}");

                    int sent = Socket.Send(bytes);
                    Console.WriteLine($"DEBUG_SERVER: Отправлено байт: {sent} из {bytes.Length} для {Nickname}");

                    if (sent != bytes.Length)
                    {
                        Console.WriteLine($"DEBUG_SERVER: ОШИБКА: не все байты отправлены!");
                    }
                }
                else
                {
                    Console.WriteLine($"DEBUG_SERVER: Сокет не подключен для {Nickname}");
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"DEBUG_SERVER: SocketException для {Nickname}: {ex.Message}");
                Console.WriteLine($"DEBUG_SERVER: ErrorCode: {ex.ErrorCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG_SERVER: Ошибка отправки для {Nickname}: {ex.Message}");
            }
        }

        public void SendMessage(string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            SendPacket(ExplodingKittensProtocol.GAME_UPDATE, bytes);
        }

        public void ResetTurnState()
        {
            DrewCardThisTurn = false;
            PlayedCardThisTurn = false;
            MustDrawCard = false;
            IsCurrentTurn = false;
        }

        public bool HasCardType(CardType type) => Hand.Any(c => c.Type == type);
        public bool HasCardId(int id) => Hand.Any(c => c.Id == id);

        public Card GetCardById(int id) => Hand.FirstOrDefault(c => c.Id == id);
        public Card GetCardByType(CardType type) => Hand.FirstOrDefault(c => c.Type == type);

        public void AddCard(Card card)
        {
            Hand.Add(card);
            if (card.Type == CardType.Defuse)
                HasDefuse = true;
        }

        public bool RemoveCard(Card card)
        {
            var removed = Hand.Remove(card);
            if (removed && card.Type == CardType.Defuse)
            {
                HasDefuse = Hand.Any(c => c.Type == CardType.Defuse);
            }
            return removed;
        }

        public bool RemoveCardById(int id)
        {
            var card = GetCardById(id);
            return card != null && RemoveCard(card);
        }

        public void Eliminate()
        {
            IsAlive = false;
            Hand.Clear();
            Console.WriteLine($"Игрок {Nickname} выбыл из игры");
        }
    }
}