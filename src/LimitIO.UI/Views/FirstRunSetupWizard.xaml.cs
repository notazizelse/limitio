using System.Windows;
using LimitIO.UI.Ipc;

namespace LimitIO.UI.Views;

public partial class FirstRunSetupWizard : Window
{
    private const int MinimumPasswordLength = 6;

    private readonly ServiceClient _client;

    public FirstRunSetupWizard(ServiceClient client)
    {
        _client = client;
        InitializeComponent();
    }

    private async void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var confirm = ConfirmBox.Password;

        if (password.Length < MinimumPasswordLength)
        {
            ShowError($"Password must be at least {MinimumPasswordLength} characters.");
            return;
        }

        if (password != confirm)
        {
            ShowError("Passwords don't match.");
            return;
        }

        ContinueButton.IsEnabled = false;
        try
        {
            var success = await _client.SetInitialPasswordAsync(password);
            if (!success)
            {
                ShowError("Couldn't set the password - the LimitIO service may not be running.");
                return;
            }

            DialogResult = true;
            Close();
        }
        finally
        {
            ContinueButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
