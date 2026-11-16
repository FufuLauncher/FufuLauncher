/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Services.CodeSigning;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

internal static class TrustInstallFlow
{
    internal static async Task<TrustOperationResult?> InstallAsync(
        CodeSigningTrustService service, XamlRoot xamlRoot, TrustStoreScope scope)
    {
        var package = await Task.Run(() => service.GetPackage(true));
        if (package == null)
        {
            await ShowAsync(xamlRoot, "ModTrust_InstallFailedTitle".GetLocalized(), "ModTrust_CertUnavailable".GetLocalized());
            return null;
        }

        var scopeText = scope == TrustStoreScope.CurrentUser ? "ModTrust_ScopeUser".GetLocalized() : "ModTrust_ScopeMachine".GetLocalized();
        var confirmed = await ShowAsync(xamlRoot, "ModTrust_ReviewTitle".GetLocalized(), BuildReviewContent(package, scopeText),
            "ModTrust_ConfirmAndInstall".GetLocalized(), "CancelBtn".GetLocalized());

        if (confirmed != ContentDialogResult.Primary) return null;

        var result = await Task.Run(() => service.InstallRoot(scope));

        if (!result.Ok && result.NeedsElevation)
        {
            result = await RunElevatedAsync(xamlRoot, "install",
                scope == TrustStoreScope.CurrentUser ? "user" : "machine");
        }

        return result;
    }

    internal static async Task<TrustOperationResult?> UninstallAsync(
        CodeSigningTrustService service, XamlRoot xamlRoot, TrustStoreScope scope)
    {
        var scopeText = scope == TrustStoreScope.CurrentUser ? "ModTrust_ScopeUser".GetLocalized() : "ModTrust_ScopeMachine".GetLocalized();
        var confirmed = await ShowAsync(xamlRoot, "ModTrust_UninstallCertTitle".GetLocalized(),
            string.Format("ModTrust_UninstallConfirm".GetLocalized(), scopeText),
            "ModTrust_UninstallAction".GetLocalized(), "CancelBtn".GetLocalized());

        if (confirmed != ContentDialogResult.Primary) return null;

        var result = await Task.Run(() => service.UninstallRoot(scope));

        if (!result.Ok && result.NeedsElevation)
        {
            result = await RunElevatedAsync(xamlRoot, "uninstall",
                scope == TrustStoreScope.CurrentUser ? "user" : "machine");
        }

        return result;
    }

    private static FrameworkElement BuildReviewContent(VerifiedTrustPackage package, string scopeText)
    {
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = string.Format("ModTrust_InstallReviewLead_Format".GetLocalized(), scopeText),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(BuildCertificateBlock("ModTrust_RootCertLabel".GetLocalized(), package.Root));
        panel.Children.Add(BuildCertificateBlock("ModTrust_IssuerCertLabel".GetLocalized(), package.Issuer));

        panel.Children.Add(new TextBlock
        {
            Text = "ModTrust_CertPurposeNote".GetLocalized(),
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        });

        return new ScrollViewer
        {
            Content = panel,
            MaxHeight = 440,
            MinWidth = 460,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static FrameworkElement BuildCertificateBlock(string title, X509Certificate2 certificate)
    {
        var stack = new StackPanel { Spacing = 4 };

        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(BuildCertificateField("ModTrust_SubjectLabel".GetLocalized(), certificate.Subject));
        stack.Children.Add(BuildCertificateField("ModTrust_IssuerLabel".GetLocalized(), certificate.Issuer));
        stack.Children.Add(BuildCertificateField("ModTrust_ThumbprintSha256".GetLocalized(),
            ManifestSignatureVerifier.Sha256Thumbprint(certificate)));
        stack.Children.Add(BuildCertificateField("ModTrust_ValidUntil".GetLocalized(), certificate.NotAfter.ToString("yyyy-MM-dd")));

        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = TryGetCardBrush(),
            Child = stack
        };
    }

    private static FrameworkElement BuildCertificateField(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Top
        };

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };

        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);

        return grid;
    }

    private static Microsoft.UI.Xaml.Media.Brush? TryGetCardBrush()
    {
        try
        {
            return Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var brush)
                ? brush as Microsoft.UI.Xaml.Media.Brush
                : null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("TrustInstallFlow", $"[TrustInstallFlow] Failed to read theme brushes: {ex.Message}");
            return null;
        }
    }

    private static async Task<ContentDialogResult> ShowAsync(
        XamlRoot xamlRoot, string title, object content, string? primaryText = null, string? closeText = null)
    {
        var resolvedPrimary = primaryText ?? "Common_Confirm".GetLocalized();
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = resolvedPrimary,
            CloseButtonText = closeText ?? "CloseBtn".GetLocalized(),
            DefaultButton = primaryText == null ? ContentDialogButton.Close : ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            Resources = { ["ContentDialogMaxWidth"] = 640.0 }
        };

        try
        {
            return await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            await Task.Delay(300);

            try
            {
                return await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                DebugLog.Info("TrustInstallFlow", $"[TrustInstallFlow] Failed to show dialog: {ex.Message}");
                return ContentDialogResult.None;
            }
        }
    }

    private static async Task<TrustOperationResult> RunElevatedAsync(XamlRoot xamlRoot, string action, string scope)
    {
        var resultFile = Path.Combine(Path.GetTempPath(), $"fufu-trust-{Guid.NewGuid():N}.json");

        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return new TrustOperationResult { Ok = false, Message = "ModTrust_ExePathUnavailable".GetLocalized() };
            }

            var process = Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = $"--trust-cert {action} {scope} --result-file \"{resultFile}\" --lang \"{ResourceExtensions.CurrentCulture}\"",
                Verb = "runas",
                UseShellExecute = true
            });

            if (process == null)
            {
                return new TrustOperationResult { Ok = false, Message = "ModTrust_ElevatedStartFailed".GetLocalized() };
            }

            await process.WaitForExitAsync();

            if (File.Exists(resultFile))
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile));
                var root = document.RootElement;

                return new TrustOperationResult
                {
                    Ok = root.TryGetProperty("ok", out var ok) && ok.GetBoolean(),
                    Message = root.TryGetProperty("message", out var message)
                        ? message.GetString() ?? string.Empty
                        : string.Empty
                };
            }

            return new TrustOperationResult
            {
                Ok = process.ExitCode == 0,
                Message = process.ExitCode == 0
                    ? "ModTrust_OpCompleted".GetLocalized()
                    : string.Format("ModTrust_ElevatedExitCode_Format".GetLocalized(), process.ExitCode)
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new TrustOperationResult { Ok = false, Message = "ModTrust_ElevationCancelled".GetLocalized() };
        }
        catch (Exception ex)
        {
            DebugLog.Info("TrustInstallFlow", $"[TrustInstallFlow] Elevated certificate operation failed: {ex}");
            return new TrustOperationResult
            {
                Ok = false,
                Message = string.Format("Common_OperationFailed_Format".GetLocalized(), ex.Message)
            };
        }
        finally
        {
            try
            {
                if (File.Exists(resultFile)) File.Delete(resultFile);
            }
            catch (Exception ex)
            {
                DebugLog.Info("TrustInstallFlow", $"[TrustInstallFlow] Failed to clean up result file: {ex.Message}");
            }
        }
    }
}