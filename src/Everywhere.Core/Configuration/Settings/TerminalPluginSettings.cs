using CommunityToolkit.Mvvm.ComponentModel;
using ShadUI;

namespace Everywhere.Configuration;

[GeneratedSettingsItems]
public sealed partial class TerminalPluginSettings : ObservableObject
{
    [DynamicLocaleKey(
        LocaleKey.TerminalPluginSettings_ShellPath_Header,
        LocaleKey.TerminalPluginSettings_ShellPath_Description)]
    [SettingsStringItem]
    [ObservableProperty]
    public partial string? ShellPath { get; set; }

    [DynamicLocaleKey(
        LocaleKey.TerminalPluginSettings_BypassApproval_Header,
        LocaleKey.TerminalPluginSettings_BypassApproval_Description)]
    public bool BypassesApproval
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;

            if (value)
            {
                ToastManager.Warning(
                    LocaleKey.Common_Warning.I18N(),
                    LocaleKey.TerminalPluginSettings_BypassesApproval_WarningToast_Content.I18N());
            }
        }
    }
}