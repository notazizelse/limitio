using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LimitIO.UI.Interop;
using LimitIO.UI.Ipc;

namespace LimitIO.UI.Views;

public partial class PasswordPromptWindow : Window
{
    private readonly ServiceClient _client;
    private bool _busy;

    public string? SessionToken { get; private set; }

    public PasswordPromptWindow(ServiceClient client)
    {
        _client = client;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            WindowActivator.ForceToForeground(this);
            PasswordBox.Focus();
        };
    }

    private async void UnlockButton_Click(object sender, RoutedEventArgs e) => await TryUnlockAsync();

    private async void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await TryUnlockAsync();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task TryUnlockAsync()
    {
        if (_busy)
        {
            return;
        }

        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        _busy = true;
        UnlockButton.IsEnabled = false;
        HideError();

        try
        {
            var result = await _client.VerifyPasswordAsync(password);

            if (result.Success && result.SessionToken is not null)
            {
                SessionToken = result.SessionToken;
                DialogResult = true;
                Close();
                return;
            }

            if (result.RetryAfterMs is { } waitMs && waitMs > 0)
            {
                ShowError($"Too many attempts - try again in {Math.Ceiling(waitMs / 1000.0)} seconds.");
            }
            else
            {
                ShowError("Incorrect password.");
            }

            PasswordBox.Clear();
            PasswordBox.Focus();
        }
        finally
        {
            _busy = false;
            UnlockButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
