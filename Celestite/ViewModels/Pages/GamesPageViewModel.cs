using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;
using Celestite.Dialogs.Games;
using Celestite.I18N;
using Celestite.Network;
using Celestite.Network.Models;
using Celestite.Utils;
using Celestite.Utils.Interop.Windows;
using Celestite.ViewModels;
using Celestite.ViewModels.Dialogs;
using Celestite.ViewModels.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cysharp.Threading.Tasks;
using FluentAvalonia.UI.Controls;

namespace Celestite.ViewModels.Pages
{
    public enum GameType : int
    {
        Download = 0, Browser = 1
    }

    public partial class GamesPageViewModel : ViewModelBase
    {
        [ObservableProperty] private AvaloniaList<MyGame> _gamesSource = [];
        [ObservableProperty] private AvaloniaList<MyGame> _games = [];
        [ObservableProperty] private GameType _selectedIndex = GameType.Download;

        [ObservableProperty] private bool _isOnlyShowViewable = true;
        [ObservableProperty] private string _filterText = string.Empty;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowLibraryStatus), nameof(LibraryStatus))]
        private bool _isLibraryLoading;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowLibraryStatus), nameof(LibraryStatus))]
        private bool _libraryLoadFailed;
        public bool ShowLibraryStatus => Games.Count == 0;
        public string LibraryStatus => IsLibraryLoading ? Localization.LibraryLoading :
            LibraryLoadFailed ? Localization.LibraryLoadFailed : Localization.LibraryEmpty;
        public bool SafeIconEnabled { get; set; } = ConfigUtils.GetSafeFanzaIcon();

        private static GameCatalogDialogViewModel? _permanentAddClientGameDialogViewModel;
        private static AddBrowserGameDialogViewModel? _permanentAddBrowserGameDialogViewModel;

        private static GameCatalogDialogViewModel PermanentAddClientGameDialogViewModel => LazyInitializer.EnsureInitialized(ref _permanentAddClientGameDialogViewModel, () => new());
        private static AddBrowserGameDialogViewModel PermanentAddBrowserGameDialogViewModel => LazyInitializer.EnsureInitialized(ref _permanentAddBrowserGameDialogViewModel, () => new());

        private static ContentDialog? _addGameDialog = null;
        private static ContentDialog? _addBrowserGameDialog = null;
        private static ContentDialog? _agreementDialog = null;
        private static ContentDialog? _hardwareRegDialog = null;
        public static AutoCompleteFilterPredicate<object> AutoCompleteFilter =>
            (search, item) =>
            {
                if (string.IsNullOrEmpty(search) || item is not MyGame x) return false;
                return MatchesLibrarySearch(x, search);
            };

        private static bool MatchesLibrarySearch(MyGame game, string query) =>
            query.Length == 0 ||
            game.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
            game.ProductId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
            game.TitleRuby?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
            (!string.IsNullOrEmpty(game.TitleRuby) && game.Romaji.Contains(query, StringComparison.OrdinalIgnoreCase));

        private static ContentDialog AddGameDialog => LazyInitializer.EnsureInitialized(ref _addGameDialog, () =>
            CreateCatalogDialog(Localization.AddClientGameDialogTitle, PermanentAddClientGameDialogViewModel, new AddClientGameDialog()));

        private static ContentDialog AddBrowserGameDialog => LazyInitializer.EnsureInitialized(ref _addBrowserGameDialog, () =>
            CreateCatalogDialog(Localization.AddBrowserGameDialogTitle, PermanentAddBrowserGameDialogViewModel, new AddBrowserGameDialog()));

        private static ContentDialog CreateCatalogDialog(string title, GameCatalogDialogViewModel viewModel, Control content)
        {
            content.DataContext = viewModel;
            var dialog = new ContentDialog
            {
                IsVisible = false,
                Title = title,
                PrimaryButtonText = Localization.CatalogAddSelected,
                IsPrimaryButtonEnabled = viewModel.SelectedGame != null,
                CloseButtonText = Localization.ButtonCancel,
                DefaultButton = ContentDialogButton.Primary,
                Content = content
            };
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GameCatalogDialogViewModel.SelectedGame))
                    dialog.IsPrimaryButtonEnabled = viewModel.SelectedGame != null;
            };
            return dialog;
        }

        public static ContentDialog AgreementDialog => LazyInitializer.EnsureInitialized(ref _agreementDialog, () => Dispatcher.UIThread.Invoke(() => new ContentDialog()
        {
            IsVisible = false,
            PrimaryButtonText = Localization.AgreementButtonAgree,
            CloseButtonText = Localization.FormClose,
            IsPrimaryButtonEnabled = true,
            DefaultButton = ContentDialogButton.Primary,
            IsHitTestVisible = false,
            Content = new AgreementCheckDialog()
        }));
        public static ContentDialog HardwareRegDialog => LazyInitializer.EnsureInitialized(ref _hardwareRegDialog, () => Dispatcher.UIThread.Invoke(() => new ContentDialog()
        {
            IsVisible = false,
            Title = Localization.HardwareRegDialogTitle,
            PrimaryButtonText = Localization.HardwareRegButton,
            CloseButtonText = Localization.ButtonCancel,
            IsPrimaryButtonEnabled = true,
            DefaultButton = ContentDialogButton.Primary,
            IsHitTestVisible = false,
            Content = new HardwareRegDialog()
        }));

        public GamesPageViewModel()
        {
            DmmGamePlayerApiHelper.AdultModeChangedEvent += (_, _) =>
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    PermanentAddClientGameDialogViewModel.Games = [];
                    PermanentAddBrowserGameDialogViewModel.Games = [];
                });
            };
            DmmGamePlayerApiHelper.ReGetGamesRequiredEvent += (_, _) => Dispatcher.UIThread.Invoke(RemoteUpdate);
            DmmGamePlayerApiHelper.LoginSessionChangedEvent += (_, _) => Dispatcher.UIThread.Invoke(RemoteUpdate);
            MyGame.GameStatusChangedEvent += (_, _) => Dispatcher.UIThread.Invoke(NotifyCollectionChanged);
            ConfigUtils.OnSafeFanzaIconChanged += (_, _) => SafeIconEnabled = ConfigUtils.GetSafeFanzaIcon();

            DmmGamePlayerApiResult.NotBroadcastErrorOccured += async (_, args) =>
            {
                switch (args.ErrorCode)
                {
                    case DmmGamePlayerApiErrorCode.NOT_CONSENT_AGREEMENT:
                        await InvokeNotConsentAgreement(args.ProductId, args.GameType);
                        return;
                    case DmmGamePlayerApiErrorCode.DEVICE_CHECK_FAILURE:
                        await InvokeDeviceAuth();
                        return;
                }
            };

            GamesSource.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                    GCUtils.CollectGeneration2();
                GamesShouldBeChanged();
            };

            RemoteUpdate();
        }

        private void GamesShouldBeChanged()
        {
            Games.Clear();
            var games = GamesSource
                .Where(x => x.IsView == IsOnlyShowViewable)
                .OrderByDescending(x => x.IsFavorite)
                .ThenByDescending(x => ConfigUtils.GetGameSettingsForcePin(x.ProductId))
                .Where(x => MatchesLibrarySearch(x, FilterText?.Trim() ?? string.Empty));
            Games.AddRange(games);
            OnPropertyChanged(nameof(ShowLibraryStatus));
        }

        public static async UniTask InvokeNotConsentAgreement(string productId, TApiGameType gameType)
        {
            var agreement = await DmmGamePlayerApiHelper.GetAgreement(productId);
            if (agreement.Failed) return;
            var vm = new AgreementDialogViewModel(agreement.Value);
            var result = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AgreementDialog.Title = agreement.Value.AgreementList[0].Title;
                AgreementDialog.DataContext = vm;
                return AgreementDialog.ShowAsync();
            });
            if (result != ContentDialogResult.Primary) return; // 同意
            var resp = await DmmGamePlayerApiHelper.ConfirmAgreement(productId, vm.IsNotification,
                vm.IsProfileApp);
            if (resp.Failed) return;
            await LaunchHelper.LaunchGame(productId, gameType);
        }
        private static async UniTask InvokeDeviceAuth()
        {
            var dataContext = new HardwareRegDialogViewModel();
            var result = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HardwareRegDialog.DataContext = dataContext;
                return HardwareRegDialog.ShowAsync();
            });
            if (result != ContentDialogResult.Primary) return;
            var result2 =
                await DmmGamePlayerApiHelper.ConfirmHardwareAuthCode(dataContext.Hostname, dataContext.VerificationCode);
            if (result2.Failed) return;
        }

        [RelayCommand]
        private void NotifyCollectionChanged() => GamesShouldBeChanged();
        partial void OnFilterTextChanged(string value) => NotifyCollectionChanged();
        [RelayCommand]
        private void RemoteUpdate() => UpdateGameList(SelectedIndex).Forget();

        private CancellationTokenSource? _cancellationTokenSource = null;
        private async UniTask UpdateGameList(GameType gameType)
        {
            var previousRequest = _cancellationTokenSource;
            using var request = new CancellationTokenSource();
            _cancellationTokenSource = request;
            previousRequest?.Cancel();
            IsLibraryLoading = true;
            LibraryLoadFailed = false;
            try
            {
                var gameList = gameType switch
                {
                    GameType.Download => await DmmGamePlayerApiHelper.MyGameList(request.Token),
                    GameType.Browser => await DmmGamePlayerApiHelper.MyBrowserGameList(request.Token),
                    _ => throw new NotImplementedException()
                };
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (!ReferenceEquals(_cancellationTokenSource, request)) return;
                    if (gameList.Failed)
                    {
                        LibraryLoadFailed = true;
                        return;
                    }
                    GamesSource.Clear();
                    GamesSource.AddRange(gameList.Value);
                });
            }
            finally
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (ReferenceEquals(_cancellationTokenSource, request))
                    {
                        _cancellationTokenSource = null;
                        IsLibraryLoading = false;
                    }
                });
            }
        }

        partial void OnSelectedIndexChanged(GameType value)
        {
            GamesSource.Clear();
            UpdateGameList(value).Forget();
        }

        partial void OnIsOnlyShowViewableChanged(bool value)
        {
            GamesShouldBeChanged();
        }

        private static async Task<bool> LoadGameCatalog(GameCatalogDialogViewModel viewModel, bool browser)
        {
            if (viewModel.Games.Count > 0) return true;
            const int pageSize = 120;
            var games = new List<SearchGameData>();
            bool adultMode = DmmGamePlayerApiHelper.IsAdultMode;
            string article = browser ? "browser:1" : "download:1";
            for (int page = 1; ; page++)
            {
                var response = await DmmGamePlayerApiHelper.Search(REQUEST_FLOOR.FREE, article, pageSize, page);
                if (response.Failed || response.Value is not { List: { } items } catalog)
                {
                    NotificationHelper.Warn(Localization.CatalogLoadFailed);
                    return false;
                }
                if (adultMode != DmmGamePlayerApiHelper.IsAdultMode) return false;
                games.AddRange(browser ? items.Where(game => game.TransformGameId.Length != 0) : items);
                if (items.Count == 0 || (long)page * pageSize >= catalog.TotalHitCount) break;
            }
            return Dispatcher.UIThread.Invoke(() =>
            {
                if (adultMode != DmmGamePlayerApiHelper.IsAdultMode) return false;
                viewModel.Games = games.Where(game => !string.IsNullOrWhiteSpace(game.ProductId))
                    .DistinctBy(game => game.ProductId, StringComparer.OrdinalIgnoreCase).ToList();
                return true;
            });
        }

        [RelayCommand]
        private Task OpenGameCatalog() => SelectedIndex == GameType.Browser
            ? OpenAddBrowserGameDialog()
            : OpenAddClientGameDialog();

        private async Task OpenAddClientGameDialog()
        {
            var viewModel = PermanentAddClientGameDialogViewModel;
            if (!await LoadGameCatalog(viewModel, browser: false)) return;
            if (await Dispatcher.UIThread.Invoke(() =>
                {
                    viewModel.ResetSelection();
                    return AddGameDialog.ShowAsync();
                }) !=
                ContentDialogResult.Primary) return;
            if (viewModel.SelectedGame is not { } selected)
            {
                NotificationHelper.Warn(Localization.CatalogSelectGame);
                return;
            }
            var gameType = await DmmGamePlayerApiHelper.GetGameTypeFromOldKeys(selected.ProductId,
                selected.Floor, adult: DmmGamePlayerApiHelper.IsAdultMode);
            if (gameType.Failed) return;
            await DmmGamePlayerApiHelper.AddStoreProductToMyGame(gameType.Value);
        }

        private async Task OpenAddBrowserGameDialog()
        {
            var viewModel = PermanentAddBrowserGameDialogViewModel;
            if (!await LoadGameCatalog(viewModel, browser: true)) return;
            if (await Dispatcher.UIThread.Invoke(() =>
                {
                    viewModel.ResetSelection();
                    return AddBrowserGameDialog.ShowAsync();
                }) !=
                ContentDialogResult.Primary) return;
            if (viewModel.SelectedGame is not { } selected)
            {
                NotificationHelper.Warn(Localization.CatalogSelectGame);
                return;
            }
            if (!string.IsNullOrEmpty(HttpHelper.ActAuth))
            {
                ProcessUtils.OpenExternalLink(DmmBrowserGameLaunchHelper.GetBrowserGameActivationUrl(selected.TransformGameId,
                    viewModel.IsNotificationAllowed, viewModel.IsDisplayAllowed));
                NotificationHelper.Info(Localization.CatalogFinishInBrowser);
                return;
            }
            var result = await DmmBrowserGameLaunchHelper.TryAddBrowserGame(selected.TransformGameId,
                viewModel.IsNotificationAllowed, viewModel.IsDisplayAllowed);
            if (result)
                DmmGamePlayerApiHelper.ForceRaiseReGetGamesRequiredEvent();
        }
    }

}

namespace Celestite.Network.Models
{
    public partial class MyGame : ViewModelBase
    {
        [ObservableProperty, NotifyPropertyChangedFor(nameof(LaunchButtonText))]
        private bool _isLaunching;
        public string LaunchButtonText => IsLaunching ? Localization.GameLaunching : Localization.ButtonLaunch;

        [RelayCommand]
        private async Task EmulateLaunch() => await LaunchHelper.EmulateLaunch(ProductId, ApiType);

        [RelayCommand]
        private void OpenGameSettings() => GameNavigationFactory.Instance.GetFrame().NavigateFromObject(this);

        [RelayCommand]
        private async Task LaunchGame()
        {
            IsLaunching = true;
            try
            {
                await LaunchHelper.LaunchGame(ProductId, Type, PackageImageUrl);
            }
            finally
            {
                Dispatcher.UIThread.Invoke(() => IsLaunching = false);
            }
        }
    }
}
