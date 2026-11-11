using System.Windows;
using System.Windows.Controls;

namespace UniToStudyHub.Views;

public partial class SettingsView : UserControl
{
    public event RoutedEventHandler? SaveAiSettings_ClickRequested;
    private void SaveAiSettings_Click(object sender, RoutedEventArgs e) => SaveAiSettings_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? RevokeAiConsent_ClickRequested;
    private void RevokeAiConsent_Click(object sender, RoutedEventArgs e) => RevokeAiConsent_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? MoodleTokenBox_PasswordChangedRequested;
    private void MoodleTokenBox_PasswordChanged(object sender, RoutedEventArgs e) => MoodleTokenBox_PasswordChangedRequested?.Invoke(sender, e);

    public event RoutedEventHandler? ConnectMoodle_ClickRequested;
    private void ConnectMoodle_Click(object sender, RoutedEventArgs e) => ConnectMoodle_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? DisconnectMoodle_ClickRequested;
    private void DisconnectMoodle_Click(object sender, RoutedEventArgs e) => DisconnectMoodle_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? RestoreBackup_ClickRequested;
    private void RestoreBackup_Click(object sender, RoutedEventArgs e) => RestoreBackup_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? ExportAllData_ClickRequested;
    private void ExportAllData_Click(object sender, RoutedEventArgs e) => ExportAllData_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? ImportAllData_ClickRequested;
    private void ImportAllData_Click(object sender, RoutedEventArgs e) => ImportAllData_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? ResetPrivacy_ClickRequested;
    private void ResetPrivacy_Click(object sender, RoutedEventArgs e) => ResetPrivacy_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? ShowPrivacyInventory_ClickRequested;
    private void ShowPrivacyInventory_Click(object sender, RoutedEventArgs e) => ShowPrivacyInventory_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? ExportDiagnostics_ClickRequested;
    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e) => ExportDiagnostics_ClickRequested?.Invoke(sender, e);

    public event RoutedEventHandler? DeleteAllPrivacyData_ClickRequested;
    private void DeleteAllPrivacyData_Click(object sender, RoutedEventArgs e) => DeleteAllPrivacyData_ClickRequested?.Invoke(sender, e);

    public ComboBox AiProviderBox => AiProviderBoxControl;
    public TextBox AiModelBox => AiModelBoxControl;
    public PasswordBox AiApiKeyBox => AiApiKeyBoxControl;
    public CheckBox AiConsentBox => AiConsentBoxControl;
    public PasswordBox MoodleTokenBox => MoodleTokenBoxControl;
    public ComboBox PrivacyResetScopeBox => PrivacyResetScopeBoxControl;
    public CheckBox PrivacySafetyBackupBox => PrivacySafetyBackupBoxControl;
    public CheckBox PrivacyDeleteBackupsBox => PrivacyDeleteBackupsBoxControl;

    public SettingsView()
    {
        InitializeComponent();
    }
}
