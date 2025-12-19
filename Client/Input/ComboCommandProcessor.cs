using Shared.Models;
using System.Text;

namespace Client.Input;

public class ComboCommandProcessor
{
    private readonly GameClient _client;
    private readonly KittensClientHelper _helper;

    public ComboCommandProcessor(GameClient client, KittensClientHelper helper)
    {
        _client = client;
        _helper = helper;
    }

    public async Task ProcessCombo(string[] parts)
    {
        if (!_client.SessionId.HasValue)
        {
            _client.PrintError("Вы не в игре.");
            return;
        }

        if (parts.Length < 3)
        {
            ShowComboUsage();
            return;
        }

        if (!int.TryParse(parts[1], out var comboType) || (comboType != 2 && comboType != 3 && comboType != 5))
        {
            _client.PrintError("❌ Неверный тип комбо. Допустимо: 2, 3, 5");
            return;
        }

        var cardIndices = ParseCardIndices(parts[2]);
        if (cardIndices.Count != comboType)
        {
            _client.PrintError($"❌ Для комбо типа {comboType} нужно {comboType} разных карт");
            return;
        }

        var comboCards = cardIndices.Select(i => _client.Hand[i]).ToList();
        if (!ValidateComboCards(comboType, comboCards))
        {
            _client.PrintError($"❌ Выбранные карты не подходят для комбо {comboType}");
            ShowComboRules(comboType);
            return;
        }

        switch (comboType)
        {
            case 2:
                await ProcessCombo2(cardIndices, parts);
                break;
            case 3:
                await ProcessCombo3(cardIndices, parts);
                break;
            case 5:
                await ProcessCombo5(cardIndices);
                break;
        }
    }

    private async Task ProcessCombo2(List<int> cardIndices, string[] parts)
    {
        string? targetData = null;

        if (parts.Length > 3)
        {
            targetData = parts[3];
        }
        else
        {
            var selectedTarget = await _client.SelectPlayerFromList("🎯 Выберите цель для Слепого Карманника:");
            if (selectedTarget == null) return;
            targetData = selectedTarget.Id.ToString();
        }

        var cardNames = cardIndices.Select(i => _client.Hand[i].Name);
        _client.PrintInfo($"🎭 Играю комбо 2 с картами: {string.Join(", ", cardNames)}");
        await SendComboCommand(2, cardIndices, targetData);
    }

    private async Task ProcessCombo3(List<int> cardIndices, string[] parts)
    {
        string? targetData = null;

        if (parts.Length > 4)
        {
            var playerNumber = parts[3];
            var cardName = parts[4];

            if (int.TryParse(playerNumber, out var playerIndex))
            {
                var alivePlayers = _client.OtherPlayers
                    .Where(p => p.IsAlive && p.Id != _client.PlayerId)
                    .OrderBy(p => p.Name)
                    .ToList();

                if (playerIndex > 0 && playerIndex <= alivePlayers.Count)
                {
                    targetData = $"{alivePlayers[playerIndex - 1].Id}|{cardName}";
                }
                else
                {
                    _client.PrintError($"❌ Неверный номер игрока! Доступно: 1-{alivePlayers.Count}");
                    return;
                }
            }
            else
            {
                targetData = $"{playerNumber}|{cardName}";
            }

            var cardNames = cardIndices.Select(i => _client.Hand[i].Name);
            _client.PrintInfo($"🎭 Играю комбо 3 с картами: {string.Join(", ", cardNames)}");
            await SendComboCommand(3, cardIndices, targetData);
        }
        else if (parts.Length > 3 && Guid.TryParse(parts[3], out _))
        {
            _client.PrintError("❌ Для комбо 3 укажите также название карты!");
            Console.WriteLine("💡 Пример: combo 3 0,1,2 [номер_игрока] [название_карты]");
            return;
        }
        else
        {
            await ProcessCombo3WithMenu(cardIndices);
        }
    }

    private async Task ProcessCombo3WithMenu(List<int> cardIndices)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     🎣 КОМБО 3: ВРЕМЯ РЫБАЧИТЬ 🎣           ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        var selectedPlayer = await _client.SelectPlayerFromList("🎯 Выберите игрока, у которого хотите взять карту:");
        if (selectedPlayer == null) return;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n📋 ВЫБЕРИТЕ КАРТУ:");
        Console.WriteLine("══════════════════════════════════════════");
        Console.ResetColor();

        Console.WriteLine("  1. Взрывной Котенок");
        Console.WriteLine("  2. Обезвредить");
        Console.WriteLine("  3. Нет");
        Console.WriteLine("  4. Атаковать");
        Console.WriteLine("  5. Пропустить");
        Console.WriteLine("  6. Одолжение");
        Console.WriteLine("  7. Перемешать");
        Console.WriteLine("  8. Заглянуть в будущее");
        Console.WriteLine("  9. Радужный Кот");
        Console.WriteLine(" 10. Котобородач");
        Console.WriteLine(" 11. Кошка-Картошка");
        Console.WriteLine(" 12. Арбузный Котэ");
        Console.WriteLine(" 13. Такокот");
        Console.WriteLine("══════════════════════════════════════════");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\n🎯 Выберите номер карты (1-13): ");
        Console.ResetColor();

        var cardNumberInput = Console.ReadLine();
        if (!int.TryParse(cardNumberInput, out var cardNumber) || cardNumber < 1 || cardNumber > 13)
        {
            _client.PrintError("❌ Неверный номер карты. Введите число от 1 до 13");
            return;
        }

        string cardName = cardNumber switch
        {
            1 => "Взрывной Котенок",
            2 => "Обезвредить",
            3 => "Нет",
            4 => "Атаковать",
            5 => "Пропустить",
            6 => "Одолжение",
            7 => "Перемешать",
            8 => "Заглянуть в будущее",
            9 => "Радужный Кот",
            10 => "Котобородач",
            11 => "Кошка-Картошка",
            12 => "Арбузный Котэ",
            13 => "Такокот",
            _ => "Обезвредить"
        };

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ Вы выбрали карту: {cardName}");
        Console.WriteLine($"📤 Запрашиваем карту '{cardName}' у игрока {selectedPlayer.Name}");
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

        var targetData = $"{selectedPlayer.Id}|{cardName}";
        var cardNames = cardIndices.Select(i => _client.Hand[i].Name);
        _client.PrintInfo($"🎭 Играю комбо 3 с картами: {string.Join(", ", cardNames)}");

        await SendComboCommand(3, cardIndices, targetData);
    }

    private async Task ProcessCombo5(List<int> cardIndices)
    {
        var cardNames = cardIndices.Select(i => _client.Hand[i].Name);
        _client.PrintInfo($"🎭 Играю комбо 5 с картами: {string.Join(", ", cardNames)}");
        await SendComboCommand(5, cardIndices, null);
    }

    private List<int> ParseCardIndices(string indicesStr)
    {
        return indicesStr.Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => int.TryParse(s, out var i) ? i : -1)
            .Where(i => i >= 0 && i < _client.Hand.Count)
            .Distinct()
            .ToList();
    }

    private bool ValidateComboCards(int comboType, List<Card> cards)
    {
        if (cards.Count != comboType) return false;

        switch (comboType)
        {
            case 2:
                return cards[0].Type == cards[1].Type ||
                       cards[0].IconId == cards[1].IconId;
            case 3:
                return (cards[0].Type == cards[1].Type && cards[1].Type == cards[2].Type) ||
                       (cards[0].IconId == cards[1].IconId && cards[1].IconId == cards[2].IconId);
            case 5:
                return cards.Select(c => c.IconId).Distinct().Count() == 5;
            default:
                return false;
        }
    }

    private void ShowComboUsage()
    {
        Console.WriteLine("📝 Использование:");
        Console.WriteLine("  combo 2 [номера_карт через запятую] [ID_цели]");
        Console.WriteLine("  combo 3 [номера_карт через запятую] [номер_игрока] [название_карты]");
        Console.WriteLine("  combo 5 [номера_карт через запятую]");
        Console.WriteLine("💡 Примеры:");
        Console.WriteLine("  combo 2 0,1 550e8400...");
        Console.WriteLine("  combo 3 0,1,2 1 Такокот");
        Console.WriteLine("  combo 5 0,1,2,3,4");
        _client.DisplayHand();
    }

    private void ShowComboRules(int comboType)
    {
        Console.WriteLine("\n📚 Правила комбо:");
        switch (comboType)
        {
            case 2:
                Console.WriteLine("• 2 одинаковые карты ИЛИ");
                Console.WriteLine("• 2 карты с одинаковой иконкой");
                break;
            case 3:
                Console.WriteLine("• 3 одинаковые карты ИЛИ");
                Console.WriteLine("• 3 карты с одинаковой иконкой");
                break;
            case 5:
                Console.WriteLine("• 5 карт с РАЗНЫМИ иконками");
                break;
        }
    }

    private async Task SendComboCommand(int comboType, List<int> cardIndices, string? targetData)
    {
        try
        {
            await _helper.SendUseCombo(
                _client.SessionId.Value,
                _client.PlayerId,
                comboType,
                cardIndices,
                targetData
            );

            _client.PrintInfo($"✅ Команда комбо отправлена!");
            await Task.Delay(500);
            _client.DisplayHand();
        }
        catch (Exception ex)
        {
            _client.PrintError($"❌ Ошибка отправки комбо: {ex.Message}");
            _client.DisplayHand();
        }
    }
}