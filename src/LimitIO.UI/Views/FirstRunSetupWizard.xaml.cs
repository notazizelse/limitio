using System.Windows;
using LimitIO.UI.Interop;
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
        // This is normally the very first window a user ever sees, shown right after an `await` in
        // App.OnStartup (or in TrayIconManager, if first-run got skipped for some reason) - without
        // forcing foreground activation here, it can appear on screen with no real keyboard focus at
        // all, i.e. exactly "the input window isn't working, I can't write anything." This window
        // previously had no Loaded handler whatsoever, so it didn't even set logical focus to the first
        // field, let alone force the OS to actually let it receive keystrokes.
        Loaded += (_, _) =>
        {
            WindowActivator.ForceToForeground(this);
            PasswordBox.Focus();
        };
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
