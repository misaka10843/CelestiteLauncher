using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Celestite.I18N;
using Celestite.Network.DynamicProxy;
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
        Status.Text = null;
    }

    private void SaveProxy(object? sender, RoutedEventArgs e)
    {
        try
        {
            Status.Text = DynamicProxyImpl.SaveSettings(ProxyAddress.Text ?? string.Empty, PassToGames.IsChecked == true)
                ? Localization.CustomProxySaved
                : Localization.CustomProxyInvalid;
        }
        catch (Exception ex)
        {
            LogManager.GetLogger("Proxy").Warn("Unable to save proxy settings.", ex);
            Status.Text = Localization.CustomProxySaveFailed;
        }
    }
}
