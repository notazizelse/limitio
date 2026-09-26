using System.Windows;
using System.Windows.Controls;
using LimitIO.Core.Ipc;

namespace LimitIO.UI.Views;

/// <summary>Add/edit dialog for a single <see cref="RuleUpsertDto"/>. <see cref="Result"/> is set only when the user saves.</summary>
public partial class RuleEditDialog : Window
{
    public RuleUpsertDto? Result { get; private set; }

    private readonly Guid? _existingId;

    public RuleEditDialog(RuleStatusDto? existingRule = null)
    {
        InitializeComponent();

        if (existingRule is not null)
        {
            _existingId = existingRule.Id;
            Title = "Edit Limit";
            TargetTypeCombo.SelectedIndex = existingRule.TargetType == "Domain" ? 0 : 1;
            TargetBox.Text = existingRule.Target;
            BudgetBox.Text = existingRule.DailyBudgetMinutes.ToString();
            EnabledCheck.IsChecked = existingRule.Enabled;
        }

        TargetTypeCombo.SelectionChanged += (_, _) => UpdateTargetLabel();
        UpdateTargetLabel();
    }

    private void UpdateTargetLabel()
    {
        var isDomain = ((ComboBoxItem)TargetTypeCombo.SelectedItem).Tag as string == "Domain";
        TargetLabel.Text = isDomain
            ? "Domain (e.g. instagram.com):"
            : "Process name (e.g. telegram, without .exe):";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var targetType = ((ComboBoxItem)TargetTypeCombo.SelectedItem).Tag as string ?? "Domain";
        var target = TargetBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(target))
        {
            ShowError("Please enter a target.");
            return;
        }

        if (!int.TryParse(BudgetBox.Text.Trim(), out var minutes) || minutes <= 0)
        {
            ShowError("Please enter a positive number of minutes.");
            return;
        }

        Result = new RuleUpsertDto(_existingId, targetType, target, minutes, EnabledCheck.IsChecked == true);
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
