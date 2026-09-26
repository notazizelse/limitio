using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LimitIO.Core.Ipc;
using LimitIO.UI.Ipc;

namespace LimitIO.UI.Views;

public partial class SettingsWindow : Window
{
    private sealed class RuleRow
    {
        public required Guid Id { get; init; }
        public required string TargetType { get; init; }
        public required string Target { get; init; }
        public required int DailyBudgetMinutes { get; init; }
        public required int ConsumedMinutes { get; init; }
        public required bool Enabled { get; init; }
        public required bool Blocked { get; init; }
        public string StatusText => Blocked ? "Blocked" : "OK";
    }

    private readonly ServiceClient _client;
    private readonly string _sessionToken;
    private readonly ObservableCollection<RuleRow> _rows = [];
    private readonly DispatcherTimer _refreshTimer;

    public SettingsWindow(ServiceClient client, string sessionToken)
    {
        _client = client;
        _sessionToken = sessionToken;
        InitializeComponent();
        RulesGrid.ItemsSource = _rows;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();

        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var status = await _client.GetStatusAsync();
        if (status is null)
        {
            return;
        }

        var selectedId = (RulesGrid.SelectedItem as RuleRow)?.Id;

        _rows.Clear();
        foreach (var rule in status.Rules)
        {
            _rows.Add(new RuleRow
            {
                Id = rule.Id,
                TargetType = rule.TargetType,
                Target = rule.Target,
                DailyBudgetMinutes = rule.DailyBudgetMinutes,
                ConsumedMinutes = rule.ConsumedMinutes,
                Enabled = rule.Enabled,
                Blocked = rule.Blocked,
            });
        }

        if (selectedId is { } id)
        {
            RulesGrid.SelectedItem = _rows.FirstOrDefault(r => r.Id == id);
        }

        PauseStatusText.Text = status.MonitoringPaused && status.PausedUntil is { } until
            ? $"Monitoring paused until {until.ToLocalTime():t}"
            : "";
        ResumeButton.IsEnabled = status.MonitoringPaused;
    }

    private async Task<IReadOnlyList<RuleUpsertDto>> BuildCurrentUpsertListAsync()
    {
        var status = await _client.GetStatusAsync();
        return (status?.Rules ?? []).Select(r => new RuleUpsertDto(r.Id, r.TargetType, r.Target, r.DailyBudgetMinutes, r.Enabled)).ToList();
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RuleEditDialog { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var current = (await BuildCurrentUpsertListAsync()).ToList();
        current.Add(dialog.Result);
        await SaveRulesAsync(current);
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not RuleRow selected)
        {
            System.Windows.MessageBox.Show("Select a limit to edit first.", "LimitIO");
            return;
        }

        var existingDto = new RuleStatusDto(selected.Id, selected.TargetType, selected.Target,
            selected.DailyBudgetMinutes, selected.ConsumedMinutes, selected.Enabled, selected.Blocked, GraceAvailable: false);

        var dialog = new RuleEditDialog(existingDto) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var current = (await BuildCurrentUpsertListAsync()).ToList();
        var index = current.FindIndex(r => r.Id == selected.Id);
        if (index >= 0)
        {
            current[index] = dialog.Result;
        }
        await SaveRulesAsync(current);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not RuleRow selected)
        {
            System.Windows.MessageBox.Show("Select a limit to delete first.", "LimitIO");
            return;
        }

        var confirm = System.Windows.MessageBox.Show($"Remove the limit for '{selected.Target}'?", "LimitIO",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var current = (await BuildCurrentUpsertListAsync()).Where(r => r.Id != selected.Id).ToList();
        await SaveRulesAsync(current);
    }

    private async Task SaveRulesAsync(IReadOnlyList<RuleUpsertDto> rules)
    {
        var (success, error) = await _client.UpdateRulesAsync(rules, _sessionToken);
        if (!success)
        {
            System.Windows.MessageBox.Show(error ?? "Failed to save changes.", "LimitIO", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        await RefreshAsync();
    }

    private async void Pause15Button_Click(object sender, RoutedEventArgs e) => await PauseAsync(15);

    private async void Pause60Button_Click(object sender, RoutedEventArgs e) => await PauseAsync(60);

    private async Task PauseAsync(int minutes)
    {
        await _client.PauseMonitoringAsync(minutes, _sessionToken);
        await RefreshAsync();
    }

    private async void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        await _client.ResumeMonitoringAsync(_sessionToken);
        await RefreshAsync();
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordDialog(_client, _sessionToken) { Owner = this };
        dialog.ShowDialog();

        if (dialog.Changed)
        {
            // The session token this window was using just got revoked as part of the password change
            // (see RequestRouter.HandleChangePassword) - close rather than silently start failing every
            // subsequent save.
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
