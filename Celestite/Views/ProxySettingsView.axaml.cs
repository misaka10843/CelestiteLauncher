using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Celestite.I18N;
using Celestite.Network.DynamicProxy;
using FluentAvalonia.UI.Controls;
using ZeroLog;

namespace Celestite.Views;

public partial class ProxySettingsView : UserControl
{
    public ProxySettingsView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ProxyAddress.Text = DynamicProxyImpl.ProxyAddress;
        PassToGames.IsChecked = DynamicProxyImpl.PassToGames;
        Status.IsVisible = false;
        Status.IsOpen = false;
    }

    private void SaveProxy(object? sender, RoutedEventArgs e)
    {
        try
        {
            bool saved = DynamicProxyImpl.SaveSettings(ProxyAddress.Text ?? string.Empty, PassToGames.IsChecked == true);
            if (saved)
                ProxyAddress.Text = DynamicProxyImpl.ProxyAddress;
            ShowStatus(saved ? Localization.CustomProxySaved : Localization.CustomProxyInvalid,
                saved ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            LogManager.GetLogger("Proxy").Warn("Unable to save proxy settings.", ex);
            ShowStatus(Localization.CustomProxySaveFailed, InfoBarSeverity.Error);
        }
    }

    private void ClearProxy(object? sender, RoutedEventArgs e)
    {
        ProxyAddress.Text = string.Empty;
        PassToGames.IsChecked = false;
        SaveProxy(sender, e);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        Status.Message = message;
        Status.Severity = severity;
        Status.IsVisible = true;
        Status.IsOpen = true;
    }
}
