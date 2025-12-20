using Client.UI;
using Shared.Models;
using System.Text;

namespace Client.Input;

public class GameCommandProcessor
{
    private readonly GameClient _client;
    private readonly KittensClientHelper _helper;
    private readonly GameConsoleRenderer _renderer;
    private readonly Dictionary<string, Func<string[], Task>> _commands;

    public GameCommandProcessor(GameClient client, KittensClientHelper helper, GameConsoleRenderer renderer)
    {
        _client = client;
        _helper = helper;
        _renderer = renderer;
        _commands = CreateCommands();
    }

    private Dictionary<string, Func<string[], Task>> CreateCommands()
    {
        return new Dictionary<string, Func<string[], Task>>
        {
            ["create"] = HandleCreateCommand,
            ["join"] = HandleJoinCommand,
            ["start"] = HandleStartCommand,
            ["play"] = HandlePlayCommand,
            ["draw"] = HandleDrawCommand,
            ["combo"] = HandleComboCommand,
            ["nope"] = HandleNopeCommand,
            ["defuse"] = HandleDefuseCommand,
            ["hand"] = HandleHandCommand,
            ["state"] = HandleStateCommand,
            ["players"] = HandlePlayersCommand,
            ["help"] = HandleHelpCommand,
            ["favor"] = HandleFavorCommand,
            ["give"] = HandleGiveCommand,
            ["steal"] = HandleStealCommand,
            ["takediscard"] = HandleTakeDiscardCommand,
            ["end"] = HandleEndTurnCommand,
            ["clear"] = HandleClearCommand,
            ["exit"] = HandleExitCommand,
            ["quit"] = HandleExitCommand,
            ["games"] = HandleListGamesCommand,
            ["list"] = HandleListGamesCommand,
            ["quickjoin"] = HandleQuickJoinCommand
        };
    }

    public async Task ProcessCommand(string input)
    {
        if (string.IsNullOrEmpty(input)) return;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var command = parts[0].ToLower();

        if (_commands.TryGetValue(command, out var handler))
        {
            try
            {
                await handler(parts);
            }
            catch (Exception ex)
            {
                _client.PrintError($"Ошибка выполнения команды: {ex.Message}");
            }
        }
        else
        {
            _client.PrintError($"Неизвестная команда: {command}");
            _client.PrintInfo("Введите 'help' для списка команд");
        }
    }

    private async Task HandleCreateCommand(string[] parts)
    {
        var name = _client.PlayerName;
        if (string.IsNullOrEmpty(name))
        {
            _client.PrintError("Имя игрока не может быть пустым!");
            return;
        }

        _client.PrintInfo($"Создаю игру как {name}...");
        await _helper.SendCreateGame(name);
    }

    private async Task HandleJoinCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("📝 Использование:");
            Console.WriteLine("  1. По номеру: join [номер_игры]");
            Console.WriteLine("     Пример: join 1");
            Console.WriteLine();
            Console.WriteLine("  2. По ID: join [ID_игры]");
            Console.WriteLine("     Пример: join 550e8400-e29b-41d4-a716-446655440000");
            Console.WriteLine();
            Console.WriteLine("💡 Сначала посмотрите список игр командой 'games'");
            return;
        }

        string name;
        string gameIdentifier = parts[1];

        // Если указан только номер игры
        if (int.TryParse(gameIdentifier, out var gameNumber))
        {
            name = parts.Length > 2 ? parts[2] : _client.PlayerName;
            await JoinGameByNumber(gameNumber, name);
        }
        // Если указан GUID игры
        else if (Guid.TryParse(gameIdentifier, out var gameId))
        {
            name = parts.Length > 2 ? parts[2] : _client.PlayerName;
            await JoinGameById(gameId, name);
        }
        // Если первым параметром указано имя
        else
        {
            name = gameIdentifier;
            if (parts.Length < 3 || !int.TryParse(parts[2], out gameNumber))
            {
                _client.PrintError("Укажите номер игры!");
                Console.WriteLine("💡 Пример: join Иван 1");
                return;
            }
            await JoinGameByNumber(gameNumber, name);
        }
    }

    private async Task JoinGameById(Guid gameId, string name)
    {
        _client.PrintInfo($"Присоединяюсь к игре {gameId} как {name}...");
        await _helper.SendJoinGame(gameId, name);
    }

    private async Task JoinGameByNumber(int gameNumber, string name)
    {
        var selectedGame = _renderer.SelectGameByNumber(gameNumber);

        if (selectedGame == null)
        {
            _client.PrintError($"Игры с номером {gameNumber} не существует!");
            Console.WriteLine("💡 Посмотрите список игр командой 'games'");
            return;
        }

        _client.PrintInfo($"Присоединяюсь к игре #{gameNumber} ({selectedGame.CreatorName}) как {name}...");
        await _helper.SendJoinGame(selectedGame.Id, name);
    }

    private async Task HandleQuickJoinCommand(string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var gameNumber))
        {
            Console.WriteLine("📝 Использование: quickjoin [номер_игры]");
            Console.WriteLine("💡 Пример: quickjoin 1");
            Console.WriteLine("⚠️  Использует ваше текущее имя");
            return;
        }

        await JoinGameByNumber(gameNumber, _client.PlayerName);
    }

    private async Task HandleStartCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре. Сначала создайте или присоединитесь к игре.");
            return;
        }

        _client.PrintInfo("Запуск игры...");
        await _helper.SendStartGame(_client.SessionId.Value);
    }

    private async Task HandlePlayCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 2 || !int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("📝 Использование: play [номер_карты]");
            Console.WriteLine("💡 Для карты 'Одолжение' просто введите номер карты, затем выберите игрока");
            _client.DisplayHand();
            return;
        }

        if (cardIndex < 0 || cardIndex >= _client.Hand.Count)
        {
            _client.PrintError($"Неверный номер карты. Доступны номера 0-{_client.Hand.Count - 1}");
            _client.DisplayHand();
            return;
        }

        var card = _client.Hand[cardIndex];

        if (card.Type == CardType.Favor)
        {
            await HandleFavorCard(cardIndex);
            return;
        }

        string? targetPlayerId = parts.Length > 2 ? parts[2] : null;

        if (targetPlayerId != null && !Guid.TryParse(targetPlayerId, out _))
        {
            _client.PrintError("ID целевого игрока должен быть в формате GUID!");
            Console.WriteLine("💡 Пример: 550e8400-e29b-41d4-a716-446655440000");
            _client.DisplayHand();
            return;
        }

        _client.PrintInfo($"Играю карту: {card.Name}");
        await _helper.SendPlayCard(_client.SessionId.Value, _client.PlayerId, cardIndex, targetPlayerId);

        await Task.Delay(300);
        _client.DisplayHand();
    }

    private async Task HandleFavorCard(int cardIndex)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     🎭 ОДОЛЖЕНИЕ 🎭                         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        var selectedPlayer = await _client.SelectPlayerFromList("🎯 Выберите игрока, у которого хотите попросить карту:");
        if (selectedPlayer == null) return;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ Вы выбрали: {selectedPlayer.Name}");
        Console.WriteLine($"📤 Играем 'Одолжение' на игрока {selectedPlayer.Name}");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("\n💡 Подтвердите действие (Enter - да, n - нет): ");
        Console.ResetColor();

        var confirmation = Console.ReadLine();
        if (!string.IsNullOrEmpty(confirmation) && confirmation.ToLower() == "n")
        {
            _client.PrintInfo("❌ Действие отменено.");
            return;
        }

        await Task.Delay(500);
        _client.PrintInfo($"Играю карту 'Одолжение' на {selectedPlayer.Name}");
        await _helper.SendPlayCard(_client.SessionId.Value, _client.PlayerId, cardIndex, selectedPlayer.Id.ToString());

        await Task.Delay(300);
        _client.DisplayHand();
    }

    private async Task HandleDrawCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        _client.PrintInfo("Беру карту из колоды...");
        await _helper.SendDrawCard(_client.SessionId.Value, _client.PlayerId);

        await Task.Delay(300);
        _client.DisplayHand();
    }

    private async Task HandleComboCommand(string[] parts)
    {
        var comboProcessor = new ComboCommandProcessor(_client, _helper);
        await comboProcessor.ProcessCombo(parts);
    }

    private async Task HandleNopeCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length == 1)
        {
            _client.PrintInfo("🚫 Играю карту НЕТ на последнее действие...");
            await _helper.SendPlayNope(_client.SessionId.Value, _client.PlayerId, Guid.Empty);
        }
        else
        {
            if (Guid.TryParse(parts[1], out var actionId))
            {
                _client.PrintInfo($"🚫 Играю НЕТ на действие {actionId}");
                await _helper.SendPlayNope(_client.SessionId.Value, _client.PlayerId, actionId);
            }
            else
            {
                _client.PrintError("Неверный формат команды!");
                Console.WriteLine("💡 Используйте просто: nope");
            }
        }
    }

    private async Task HandleDefuseCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue || _client.PlayerId == Guid.Empty)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        var hasDefuseCard = _client.Hand.Any(c => c.Type == CardType.Defuse);
        if (!hasDefuseCard)
        {
            _client.PrintError("❌ У вас нет карты 'Обезвредить' в руке!");
            _client.DisplayHand();
            return;
        }

        if (parts.Length == 1)
        {
            _client.PrintInfo("💣 Обезвреживаю котенка...");
            await _helper.SendPlayDefuse(_client.SessionId.Value, _client.PlayerId);

            await Task.Delay(300);
            _client.DisplayHand();
        }
        else
        {
            _client.PrintError("❌ Неверная команда!");
            Console.WriteLine("💡 Используйте просто: defuse");
            _client.DisplayHand();
        }
    }

    private Task HandleHandCommand(string[] parts)
    {
        _client.DisplayHand();
        return Task.CompletedTask;
    }

    private async Task HandleStateCommand(string[] parts)
    {
        if (_client.SessionId.HasValue)
            await _helper.SendGetGameState(_client.SessionId.Value);
        else
            _client.PrintError("Вы не в игре.");
    }

    private Task HandlePlayersCommand(string[] parts)
    {
        _client.DisplayPlayers();
        return Task.CompletedTask;
    }

    private Task HandleHelpCommand(string[] parts)
    {
        _renderer.DisplayHelp();
        return Task.CompletedTask;
    }

    private async Task HandleFavorCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 4)
        {
            Console.WriteLine("📝 Использование: give [номер_карты]");
            Console.WriteLine($"💡 Пример: give 0");
            return;
        }

        if (!Guid.TryParse(parts[1], out var gameId) || gameId != _client.SessionId.Value)
        {
            _client.PrintError("Неверный ID игры");
            return;
        }

        if (!Guid.TryParse(parts[2], out var playerId) || playerId != _client.PlayerId)
        {
            _client.PrintError("Неверный ваш ID");
            return;
        }

        if (!int.TryParse(parts[3], out var cardIndex))
        {
            _client.PrintError("Неверный номер карты");
            _client.DisplayHand();
            return;
        }

        if (cardIndex < 0 || cardIndex >= _client.Hand.Count)
        {
            _client.PrintError($"Неверный номер карты! У вас {_client.Hand.Count} карт (0-{_client.Hand.Count - 1})");
            _client.DisplayHand();
            return;
        }

        var card = _client.Hand[cardIndex];
        _client.PrintInfo($"📤 Отдаю карту #{cardIndex}: {card.Name}");
        await _helper.SendFavorResponse(gameId, playerId, cardIndex);
    }

    private async Task HandleGiveCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 2 || !int.TryParse(parts[1], out var cardIndex))
        {
            Console.WriteLine("📝 Использование: give [номер_карты]");
            _client.DisplayHand();
            return;
        }

        if (cardIndex < 0 || cardIndex >= _client.Hand.Count)
        {
            _client.PrintError($"Неверный номер карты! У вас {_client.Hand.Count} карт (0-{_client.Hand.Count - 1})");
            _client.DisplayHand();
            return;
        }

        var card = _client.Hand[cardIndex];
        _client.PrintInfo($"📤 Отдаю карту #{cardIndex}: {card.Name}");
        await _helper.SendFavorResponse(_client.SessionId.Value, _client.PlayerId, cardIndex);

        await Task.Delay(300);
        _client.DisplayHand();
    }

    private async Task HandleStealCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 2)
        {
            Console.WriteLine("📝 Использование: steal [номер_карты]");
            Console.WriteLine("💡 Пример: steal 2");
            return;
        }

        if (!int.TryParse(parts[1], out var cardIndex))
        {
            _client.PrintError("Неверный номер карты! Введите число.");
            return;
        }

        _client.PrintInfo($"🎭 Краду карту #{cardIndex}...");
        await _helper.SendStealCard(_client.SessionId.Value, _client.PlayerId, cardIndex);
    }

    private async Task HandleTakeDiscardCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 2)
        {
            Console.WriteLine("📝 Использование: takediscard [номер_карты]");
            Console.WriteLine("💡 Пример: takediscard 1");
            return;
        }

        if (!int.TryParse(parts[1], out var cardIndex))
        {
            _client.PrintError("Неверный номер карты! Введите число.");
            return;
        }

        _client.PrintInfo($"🎨 Беру карту #{cardIndex} из сброса...");
        await _helper.SendTakeFromDiscard(_client.SessionId.Value, _client.PlayerId, cardIndex);
    }

    private async Task HandleEndTurnCommand(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        _client.PrintInfo("Завершаю ход...");
        await _helper.SendEndTurn(_client.SessionId.Value, _client.PlayerId);
    }

    private Task HandleClearCommand(string[] parts)
    {
        Console.Clear();
        _renderer.PrintHeader();
        return Task.CompletedTask;
    }

    private Task HandleExitCommand(string[] parts)
    {
        _client.Running = false;
        return Task.CompletedTask;
    }

    private async Task HandleListGamesCommand(string[] parts)
    {
        await _helper.SendGetAvailableGames();
        _client.PrintInfo("🔍 Обновляю список игр...");
    }
}