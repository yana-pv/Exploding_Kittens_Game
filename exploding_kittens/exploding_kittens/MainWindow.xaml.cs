using exploding_kittens.Networking;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace exploding_kittens
{
    public partial class MainWindow : Window
    {
        private NetworkClient _client = new NetworkClient();

        public MainWindow()
        {
            InitializeComponent();
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            _client.OnMessageReceived += HandleMessage;
            _client.OnErrorReceived += HandleError;
            _client.OnGameJoined += HandleGameJoined;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var scaleAnimation = new DoubleAnimation
            {
                To = 0.95,
                Duration = TimeSpan.FromMilliseconds(100),
                AutoReverse = true
            };
            ConnectButton.RenderTransform = new ScaleTransform(1, 1);
            ConnectButton.RenderTransformOrigin = new Point(0.5, 0.5);
            ConnectButton.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            ConnectButton.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

            ConnectToServer();
        }

        private async void ConnectToServer()
        {
            var serverInfo = IpTextBox.Text.Split(':');
            if (serverInfo.Length != 2 || !int.TryParse(serverInfo[1], out int port))
            {
                MessageBox.Show("Неверный формат IP-адреса. Используйте: 127.0.0.1:7777");
                return;
            }

            var playerName = NicknameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(playerName))
            {
                playerName = $"Игрок{new Random().Next(1000)}";
            }

            GameSettings.PlayerName = playerName;
            GameSettings.ServerIP = IpTextBox.Text;
            GameSettings.PlayersCount = PlayersComboBox.SelectedIndex + 2; // 2-5 игроков

            var connected = await _client.ConnectAsync(serverInfo[0], port);
            if (connected)
            {
                // Создаем новую игру
                await _client.CreateGameAsync(playerName);
            }
            else
            {
                MessageBox.Show("Не удалось подключиться к серверу");
            }
        }

        private void HandleMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                // Можно показывать сообщения в отдельном окне или логе
                if (message.Contains("Игра создана!"))
                {
                    // Переходим в игровое окно
                    var gameWindow = new GameWindow(_client);
                    gameWindow.Show();
                    this.Close();
                }
            });
        }

        private void HandleError(string error)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(error, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void HandleGameJoined(Guid gameId, Guid playerId)
        {
            Dispatcher.Invoke(() =>
            {
                // Сохраняем ID игры и игрока
                GameSettings.GameId = gameId;
                GameSettings.PlayerId = playerId;
            });
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            _client.Disconnect();
            base.OnClosed(e);
        }
    }
}