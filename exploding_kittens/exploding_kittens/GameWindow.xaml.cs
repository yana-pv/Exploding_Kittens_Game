using exploding_kittens.ClientModels;
using exploding_kittens.Dialogs;
using exploding_kittens.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace exploding_kittens
{
    public partial class GameWindow : Window
    {
        private NetworkClient _client;
        private List<ClientCardDto> _currentHand = new List<ClientCardDto>();
        private GameStateDto _currentGameState;
        private DispatcherTimer _timer;
        private int _timeLeft = 30;
        private bool _isMyTurn = false;

        // Элементы для отображения карт игроков
        private List<Border> _playerCards = new List<Border>();
        private List<PlayerDisplayInfo> _otherPlayers = new List<PlayerDisplayInfo>();

        public GameWindow(NetworkClient client)
        {
            InitializeComponent();
            _client = client;
            SetupEventHandlers();
            InitializeGame();
            SetupTimer();
        }

        private void SetupEventHandlers()
        {
            _client.OnMessageReceived += HandleMessage;
            _client.OnHandUpdated += HandleHandUpdated;
            _client.OnGameStateUpdated += HandleGameStateUpdated;
            _client.OnErrorReceived += HandleError;
        }

        private void InitializeGame()
        {
            // Запрашиваем начальное состояние
            if (GameSettings.GameId != Guid.Empty)
            {
                _ = _client.GetGameStateAsync(GameSettings.GameId);
            }
        }

        private void GameWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Анимация появления
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.5)
            };
            this.BeginAnimation(OpacityProperty, fadeIn);

            // Инициализация игры
            InitializeGame();

            // Настройка UI
            SetupUI();
        }

        private void SetupUI()
        {
            // Инициализация UI элементов
            if (TimerLabel != null)
                TimerLabel.Text = "00:30";

            if (CurrentPlayerLabel != null)
                CurrentPlayerLabel.Text = GameSettings.PlayerName ?? "Игрок";

            if (MessageLogText != null)
                MessageLogText.Text = $"Добро пожаловать, {GameSettings.PlayerName}!\n";

            // Убедимся, что кнопки отключены в начале
            if (DrawCardButton != null)
                DrawCardButton.IsEnabled = false;

            if (PlayCardButton != null)
                PlayCardButton.IsEnabled = false;

            if (EndTurnButton != null)
                EndTurnButton.IsEnabled = false;

            if (UseComboButton != null)
                UseComboButton.IsEnabled = false;
        }

        private void SetupTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_timeLeft > 0)
            {
                _timeLeft--;
                TimerLabel.Text = $"00:{_timeLeft:D2}";
            }
            else
            {
                _timer.Stop();
                // Таймаут - автоматическое действие
                if (_isMyTurn)
                {
                    // Автоматически берем карту при таймауте
                    _ = _client.DrawCardAsync(GameSettings.GameId, GameSettings.PlayerId);
                }
            }
        }

        private void HandleMessage(string message)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                // Добавляем сообщение в лог
                if (MessageLogText != null)
                {
                    MessageLogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";

                    // Прокручиваем вниз
                    var scrollViewer = GetChildOfType<ScrollViewer>(MessageLogText.Parent as FrameworkElement);
                    if (scrollViewer != null)
                    {
                        scrollViewer.ScrollToEnd();
                    }
                }

                if (message.Contains("Ваш ход!"))
                {
                    StartTurn();
                }
                else if (message.Contains("Ходит"))
                {
                    StopTurn();
                }
            }));
        }

        private void HandleHandUpdated(List<ClientCardDto> hand)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                _currentHand = hand ?? new List<ClientCardDto>();
                UpdatePlayerHandDisplay();

                // Обновляем счетчик карт
                if (MyCardCountLabel != null)
                {
                    MyCardCountLabel.Text = $"{hand.Count} шт.";
                }
            }));
        }

        private void HandleGameStateUpdated(GameStateDto state)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                _currentGameState = state;
                UpdateGameStateDisplay();
            }));
        }

        private void HandleError(string error)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                MessageBox.Show(error, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }));
        }

        private void StartTurn()
        {
            _isMyTurn = true;
            _timeLeft = 30;

            if (_timer != null)
                _timer.Start();

            // Активируем кнопки
            if (DrawCardButton != null)
                DrawCardButton.IsEnabled = true;
            if (PlayCardButton != null)
                PlayCardButton.IsEnabled = true;
            if (EndTurnButton != null)
                EndTurnButton.IsEnabled = true;
            if (UseComboButton != null)
                UseComboButton.IsEnabled = true;

            // Подсвечиваем текущего игрока
            if (CurrentPlayerLabel != null)
                CurrentPlayerLabel.Text = GameSettings.PlayerName;
        }

        private void StopTurn()
        {
            _isMyTurn = false;

            if (_timer != null)
                _timer.Stop();

            // Деактивируем кнопки
            if (DrawCardButton != null)
                DrawCardButton.IsEnabled = false;
            if (PlayCardButton != null)
                PlayCardButton.IsEnabled = false;
            if (EndTurnButton != null)
                EndTurnButton.IsEnabled = false;
            if (UseComboButton != null)
                UseComboButton.IsEnabled = false;
        }

        private void UpdatePlayerHandDisplay()
        {
            // Очищаем старые карты
            if (PlayerHandPanel != null)
            {
                PlayerHandPanel.Children.Clear();
                _playerCards.Clear();

                // Добавляем новые карты
                for (int i = 0; i < _currentHand.Count; i++)
                {
                    var card = _currentHand[i];
                    var cardBorder = CreateCardBorder(card, i);
                    _playerCards.Add(cardBorder);
                    PlayerHandPanel.Children.Add(cardBorder);
                }
            }
        }

        private Border CreateCardBorder(ClientCardDto card, int index)
        {
            var border = new Border
            {
                Width = 120,
                Height = 168,
                Margin = new Thickness(8),
                Cursor = Cursors.Hand,
                ToolTip = card.Name
            };

            try
            {
                var bitmap = new BitmapImage(new Uri(card.ImagePath, UriKind.RelativeOrAbsolute));
                border.Background = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
            }
            catch (Exception)
            {
                // Если картинка не загрузилась
                border.Background = Brushes.Gray;
            }

            // Обработка клика
            border.MouseLeftButtonDown += (s, e) => OnCardClicked(index, card);

            return border;
        }

        private void OnCardClicked(int index, ClientCardDto card)
        {
            if (!_isMyTurn) return;

            // В зависимости от типа карты предлагаем варианты действий
            if (card.Type == CardType.Attack || card.Type == CardType.Favor)
            {
                ShowTargetSelectionDialog(index, card);
            }
            else if (card.Type >= CardType.RainbowCat && card.Type <= CardType.TacoCat)
            {
                ShowComboSelectionDialog(index, card);
            }
            else
            {
                // Простая карта - играем сразу
                _ = _client.PlayCardAsync(GameSettings.GameId, GameSettings.PlayerId, index);
            }
        }

        private void ShowTargetSelectionDialog(int cardIndex, ClientCardDto card)
        {
            if (_currentGameState == null) return;

            var otherPlayers = _currentGameState.Players
                .Where(p => p.Id != GameSettings.PlayerId && p.IsAlive)
                .ToList();

            if (otherPlayers.Count == 0) return;

            var dialog = new TargetSelectionDialog(otherPlayers);
            if (dialog.ShowDialog() == true && dialog.SelectedPlayerId != null)
            {
                _ = _client.PlayCardAsync(
                    GameSettings.GameId,
                    GameSettings.PlayerId,
                    cardIndex,
                    dialog.SelectedPlayerId.Value.ToString());
            }
        }

        private void ShowComboSelectionDialog(int cardIndex, ClientCardDto card)
        {
            // Находим другие карты того же типа для комбо
            var sameTypeCards = _currentHand
                .Select((c, i) => new { Card = c, Index = i })
                .Where(x => x.Card.Type == card.Type && x.Index != cardIndex)
                .ToList();

            if (sameTypeCards.Count >= 1) // Есть как минимум 2 одинаковые
            {
                var dialog = new ComboSelectionDialog(card, sameTypeCards.Select(x => x.Card).ToList());
                if (dialog.ShowDialog() == true)
                {
                    // Формируем индексы для комбо
                    var indices = new List<int> { cardIndex };
                    indices.AddRange(dialog.SelectedIndices);

                    _ = _client.UseComboAsync(
                        GameSettings.GameId,
                        GameSettings.PlayerId,
                        2, // Комбо из 2 карт
                        string.Join(",", indices),
                        dialog.TargetPlayerId != null ? dialog.TargetPlayerId.ToString() : null);
                }
            }
            else
            {
                MessageBox.Show("Нет других карт того же типа для комбо", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateGameStateDisplay()
        {
            if (_currentGameState == null) return;

            // Обновляем информацию о игроках
            if (CurrentPlayerLabel != null)
                CurrentPlayerLabel.Text = _currentGameState.CurrentPlayerName ?? "";

            if (PlayersCountLabel != null)
                PlayersCountLabel.Text = _currentGameState.AlivePlayers.ToString();

            // Обновляем колоду
            if (DeckCountLabel != null)
                DeckCountLabel.Text = _currentGameState.CardsInDeck.ToString();

            if (DeckCountText != null)
                DeckCountText.Text = _currentGameState.CardsInDeck.ToString();

            // Обновляем информацию о других игроках
            UpdateOtherPlayersDisplay();
        }

        private void UpdateOtherPlayersDisplay()
        {
            if (_currentGameState == null) return;

            var players = _currentGameState.Players
                .Where(p => p.Id != GameSettings.PlayerId && p.IsAlive)
                .OrderBy(p => p.TurnOrder)
                .ToList();

            // Показываем/скрываем панели игроков в зависимости от количества
            if (players.Count >= 1)
            {
                TopPlayerPanel.Visibility = Visibility.Visible;
                var topPlayer = players[0];
                TopPlayerName.Text = topPlayer.Name;
                TopPlayerCardCount.Text = $"{topPlayer.CardCount} карт";
                UpdatePlayerCardsPanel(TopPlayerCardsPanel, topPlayer.CardCount);
            }
            else
            {
                TopPlayerPanel.Visibility = Visibility.Collapsed;
            }

            if (players.Count >= 2)
            {
                LeftPlayerPanel.Visibility = Visibility.Visible;
                var leftPlayer = players.Count > 1 ? players[1] : null;
                if (leftPlayer != null)
                {
                    LeftPlayerName.Text = leftPlayer.Name;
                    LeftPlayerCardCount.Text = $"{leftPlayer.CardCount} карт";
                    UpdatePlayerCardsPanel(LeftPlayerCardsPanel, leftPlayer.CardCount);
                }
            }
            else
            {
                LeftPlayerPanel.Visibility = Visibility.Collapsed;
            }

            if (players.Count >= 3)
            {
                RightPlayerPanel.Visibility = Visibility.Visible;
                var rightPlayer = players.Count > 2 ? players[2] : null;
                if (rightPlayer != null)
                {
                    RightPlayerName.Text = rightPlayer.Name;
                    RightPlayerCardCount.Text = $"{rightPlayer.CardCount} карт";
                    UpdatePlayerCardsPanel(RightPlayerCardsPanel, rightPlayer.CardCount);
                }
            }
            else
            {
                RightPlayerPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdatePlayerCardsPanel(WrapPanel panel, int cardCount)
        {
            if (panel == null) return;

            panel.Children.Clear();

            for (int i = 0; i < Math.Min(cardCount, 10); i++) // Ограничиваем показ 10 карт
            {
                var cardBorder = new Border
                {
                    Width = 35,
                    Height = 49,
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(1),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a148c")),
                    BorderThickness = new Thickness(2)
                };

                try
                {
                    var bitmap = new BitmapImage(new Uri("photo/Shirt.png", UriKind.RelativeOrAbsolute));
                    cardBorder.Background = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
                }
                catch
                {
                    cardBorder.Background = Brushes.DarkGray;
                }

                panel.Children.Add(cardBorder);
            }

            if (cardCount > 10)
            {
                var moreText = new TextBlock
                {
                    Text = $"+{cardCount - 10}",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(moreText);
            }
        }

        private void DrawCardButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMyTurn) return;
            _ = _client.DrawCardAsync(GameSettings.GameId, GameSettings.PlayerId);
        }

        private void PlayCardButton_Click(object sender, RoutedEventArgs e)
        {
            // Кнопка для ручного выбора карты
            if (!_isMyTurn || _currentHand.Count == 0) return;

            MessageBox.Show("Выберите карту из вашей руки, кликнув по ней", "Информация",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void EndTurnButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMyTurn) return;
            _ = _client.EndTurnAsync(GameSettings.GameId, GameSettings.PlayerId);
        }

        private void UseComboButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMyTurn || _currentHand.Count < 2) return;

            // Простой диалог для выбора типа комбо
            var result = MessageBox.Show(
                "Выберите тип комбо:\n\n" +
                "Да - 2 одинаковые карты (Слепой карманник)\n" +
                "Нет - 3 одинаковые карты (Время рыбачить)\n" +
                "Отмена - 5 разных карт (Воровство из сброса)",
                "Выбор комбо",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            int comboType = 0;

            switch (result)
            {
                case MessageBoxResult.Yes:
                    comboType = 2;
                    break;
                case MessageBoxResult.No:
                    comboType = 3;
                    break;
                case MessageBoxResult.Cancel:
                    comboType = 5;
                    break;
                default:
                    return;
            }

            // Проверяем, можно ли создать комбо
            if (comboType == 2 || comboType == 3)
            {
                // Для комбо 2 или 3 нужны одинаковые карты
                var groupedCards = _currentHand
                    .GroupBy(c => c.Type)
                    .Where(g => g.Count() >= comboType)
                    .ToList();

                if (groupedCards.Count == 0)
                {
                    MessageBox.Show($"У вас нет {comboType} одинаковых карт для комбо",
                        "Невозможно создать комбо",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (comboType == 5)
            {
                // Для комбо 5 нужны 5 разных карт по иконкам
                var distinctIcons = _currentHand
                    .Select(c => c.Type)
                    .Distinct()
                    .Count();

                if (distinctIcons < 5)
                {
                    MessageBox.Show("У вас нет 5 карт с разными иконками для комбо",
                        "Невозможно создать комбо",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            MessageBox.Show($"Вы выбрали комбо из {comboType} карт. Теперь выберите карты из вашей руки, кликнув по ним",
                "Информация",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeckBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isMyTurn) return;

            // Клик по колоде - взять карту
            _ = _client.DrawCardAsync(GameSettings.GameId, GameSettings.PlayerId);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.3)
            };

            fadeOut.Completed += (s, args) =>
            {
                _client.Disconnect();
                var mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            };

            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape)
            {
                ExitButton_Click(null, null);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _client.Disconnect();
            base.OnClosed(e);
        }

        // Вспомогательный метод для поиска дочерних элементов
        private static T GetChildOfType<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T result)
                    return result;

                var childResult = GetChildOfType<T>(child);
                if (childResult != null)
                    return childResult;
            }
            return null;
        }
    }

    // Вспомогательный класс для отображения игроков
    public class PlayerDisplayInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int CardCount { get; set; }
        public bool IsCurrent { get; set; }
        public int Position { get; set; } // 0-верх, 1-лево, 2-право

        public PlayerDisplayInfo()
        {
            Name = string.Empty;
        }
    }
}