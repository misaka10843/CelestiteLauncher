using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celestite.ViewModels.Dialogs
{
    public partial class AddBrowserGameDialogViewModel : GameCatalogDialogViewModel
    {
        [ObservableProperty] private bool _isNotificationAllowed;
        [ObservableProperty] private bool _isDisplayAllowed;
    }
}
