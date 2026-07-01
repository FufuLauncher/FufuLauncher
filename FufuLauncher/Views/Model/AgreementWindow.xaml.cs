/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class AgreementWindow : WindowEx
{
    public AgreementWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        this.CenterOnScreen();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        IsShownInSwitchers = true;

        ContentFrame.Navigate(typeof(AgreementPage));

        WeakReferenceMessenger.Default.Register<AgreementAcceptedMessage>(this, (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                WeakReferenceMessenger.Default.Unregister<AgreementAcceptedMessage>(this);
                Close();
            });
        });
    }
}

