using System;
using System.Collections.Generic;
using System.Linq;
using Celestite.Network.Models;
using Celestite.I18N;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celestite.ViewModels.Dialogs
{
    public partial class GameCatalogDialogViewModel : ViewModelBase
    {
        [ObservableProperty] private string? _filterText;
        [ObservableProperty] private List<SearchGameData> _games = [];
        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ResultCount))]
        private IReadOnlyList<SearchGameData> _visibleGames = [];
        [ObservableProperty] private SearchGameData? _selectedGame;
        public bool IsEmpty => VisibleGames.Count == 0;
        public string ResultCount => string.Format(Localization.CatalogSelectionCount, VisibleGames.Count, Games.Count);

        partial void OnFilterTextChanged(string? value) => FilterGames();
        partial void OnGamesChanged(List<SearchGameData> value) => FilterGames();

        public void ResetSelection()
        {
            FilterText = string.Empty;
            SelectedGame = null;
        }

        private void FilterGames()
        {
            var query = FilterText?.Trim();
            VisibleGames = string.IsNullOrEmpty(query) ? Games : Games.Where(game =>
                game.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                game.ProductId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                game.TransformGameId.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (SelectedGame != null && !VisibleGames.Contains(SelectedGame))
                SelectedGame = null;
        }
    }
}
