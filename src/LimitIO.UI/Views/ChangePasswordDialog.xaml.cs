using System.Windows;
using LimitIO.UI.Interop;
using LimitIO.UI.Ipc;

namespace LimitIO.UI.Views;

public partial class ChangePasswordDialog : Window
{
    private const int MinimumPasswordLength = 6;

    private readonly ServiceClient _client;
    private readonly string _sessionToken;

    public bool Changed { get; private set; }

    public ChangePasswordDialog(ServiceClient client, string sessionToken)
    {
        _client = client;
        _sessionToken = sessionToken;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            WindowActivator.ForceToForeground(this);
            OldPasswordBox.Focus();
        };
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var oldPassword = OldPasswordBox.Password;
        var newPassword = NewPasswordBox.Password;
        var confirm = ConfirmBox.Password;

        if (newPassword.Length < MinimumPasswordLength)
        {
            ShowError($"New password must be at least {MinimumPasswordLength} characters.");
            return;
        }

        if (newPassword != confirm)
        {
            ShowError("New passwords don't match.");
            return;
        }

        var (success, error) = await _client.ChangePasswordAsync(oldPassword, newPassword, _sessionToken);
        if (!success)
        {
            ShowError(error ?? "Couldn't change the password.");
            return;
        }

        Changed = true;
        System.Windows.MessageBox.Show(
            "Password changed. For security, you'll need to unlock again the next time you open Settings.",
            "LimitIO", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
