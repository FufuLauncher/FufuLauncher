/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class CloudPasswordInput : UserControl
{
    private const string ShowPasswordGlyph = "\uE5F2";
    private const string HidePasswordGlyph = "\uE5F5";

    public CloudPasswordInput()
    {
        InitializeComponent();
        AutomationProperties.SetLabeledBy(Input, InputLabel);
        MaskPassword();
    }

    public string Label
    {
        get => InputLabel.Text;
        set => InputLabel.Text = value;
    }

    public string PlaceholderText
    {
        get => Input.PlaceholderText;
        set => Input.PlaceholderText = value;
    }

    public string Password
    {
        get => Input.Password;
        set => Input.Password = value;
    }

    public void FocusInput() => Input.Focus(FocusState.Programmatic);

    public void MaskPassword() => SetPasswordVisibility(visible: false);

    private void OnRevealClick(object sender, RoutedEventArgs e)
        => SetPasswordVisibility(Input.PasswordRevealMode == PasswordRevealMode.Hidden);

    private void SetPasswordVisibility(bool visible)
    {
        Input.PasswordRevealMode = visible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        RevealIcon.Glyph = visible ? HidePasswordGlyph : ShowPasswordGlyph;

        var action = (visible ? "CloudSync_HidePassword" : "CloudSync_ShowPassword").GetLocalized();
        AutomationProperties.SetName(RevealButton, action);
        ToolTipService.SetToolTip(RevealButton, action);
    }
}
