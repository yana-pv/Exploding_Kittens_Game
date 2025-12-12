using exploding_kittens.ClientModels;
using System;
using System.Collections.Generic;
using System.Windows;

namespace exploding_kittens.Dialogs
{
    public partial class ComboSelectionDialog : Window
    {
        public List<int> SelectedIndices { get; }
        public Guid? TargetPlayerId { get; private set; }
        private ClientCardDto _mainCard;
        private List<ClientCardDto> _availableCards;

        public ComboSelectionDialog(ClientCardDto mainCard, List<ClientCardDto> availableCards)
        {
            InitializeComponent();

            SelectedIndices = new List<int>();
            _mainCard = mainCard;
            _availableCards = availableCards;

            Title = $"Комбо: {mainCard.Name}";

            // Замените CardsListBox на фактическое имя вашего ListBox в XAML
            // Например, если в XAML у вас есть: <ListBox x:Name="CardsListBox">
            if (FindName("CardsListBox") is System.Windows.Controls.ListBox listBox)
            {
                listBox.ItemsSource = availableCards;
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedIndices.Clear();

            // Здесь нужно реализовать выбор карт для комбо
            // и выбор цели если нужно
            // Пример:
            /*
            if (FindName("CardsListBox") is System.Windows.Controls.ListBox listBox && 
                listBox.SelectedItems.Count > 0)
            {
                foreach (var selectedItem in listBox.SelectedItems)
                {
                    if (selectedItem is ClientCardDto card && 
                        _availableCards.IndexOf(card) >= 0)
                    {
                        SelectedIndices.Add(_availableCards.IndexOf(card));
                    }
                }
            }
            */

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}