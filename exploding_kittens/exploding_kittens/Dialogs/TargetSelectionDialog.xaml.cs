using exploding_kittens.ClientModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace exploding_kittens.Dialogs
{
    public partial class TargetSelectionDialog : Window
    {
        public Guid? SelectedPlayerId { get; private set; }

        public TargetSelectionDialog(List<PlayerInfoDto> players)
        {
            InitializeComponent();

            if (PlayersListBox != null)
            {
                PlayersListBox.ItemsSource = players;
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlayersListBox?.SelectedItem is PlayerInfoDto selectedPlayer)
            {
                SelectedPlayerId = selectedPlayer.Id;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Выберите игрока", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}