using System.Windows;
using System.Windows.Controls;
using DexcomOverlay.Models;
using DexcomOverlay.Services;

namespace DexcomOverlay;

public partial class SuppressionWindow : Window
{
    private readonly AlertSuppressionService _suppression;
    private readonly AppSettings _settings;
    private bool _initializing = true;

    public SuppressionWindow(AppSettings settings, AlertSuppressionService suppression)
    {
        InitializeComponent();
        _settings = settings;
        _suppression = suppression;

        PopulateGlobalSchedule();
        AlertTypeCombo.SelectedIndex = 0;
        RefreshSummary();

        _initializing = false;
    }

    // ── Global suppression buttons ─────────────────────────────

    private void GlobalSuppress10_Click(object sender, RoutedEventArgs e) => DoGlobalSuppress(TimeSpan.FromMinutes(10));
    private void GlobalSuppress30_Click(object sender, RoutedEventArgs e) => DoGlobalSuppress(TimeSpan.FromMinutes(30));
    private void GlobalSuppress60_Click(object sender, RoutedEventArgs e) => DoGlobalSuppress(TimeSpan.FromHours(1));
    private void GlobalSuppress120_Click(object sender, RoutedEventArgs e) => DoGlobalSuppress(TimeSpan.FromHours(2));
    private void GlobalSuppressIndefinite_Click(object sender, RoutedEventArgs e) => DoGlobalSuppress(null);

    private void GlobalClear_Click(object sender, RoutedEventArgs e)
    {
        _suppression.ClearGlobalSuppression();
        RefreshSummary();
    }

    private void DoGlobalSuppress(TimeSpan? duration)
    {
        _suppression.SuppressAll(duration);
        RefreshSummary();
    }

    // ── Global schedule ────────────────────────────────────────

    private void GlobalSchedule_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        GlobalSchedulePanel.Visibility = GlobalScheduleEnabled.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;

        SaveGlobalSchedule();
        RefreshSummary();
    }

    private void PopulateGlobalSchedule()
    {
        var prev = _initializing;
        _initializing = true;
        try
        {
            var sched = _settings.Suppression.Global.Schedule;
            if (sched is not null)
            {
                GlobalScheduleEnabled.IsChecked = sched.Enabled;
                GlobalScheduleStart.Text = sched.StartTime;
                GlobalScheduleEnd.Text = sched.EndTime;
                SetDayCheckboxes("G", sched.Days);
                GlobalSchedulePanel.Visibility = sched.Enabled ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateGlobalStatus();
        }
        finally
        {
            _initializing = prev;
        }
    }

    private void SaveGlobalSchedule()
    {
        var sched = new ScheduleSuppression
        {
            Enabled = GlobalScheduleEnabled.IsChecked == true,
            StartTime = GlobalScheduleStart.Text.Trim(),
            EndTime = GlobalScheduleEnd.Text.Trim(),
            Days = GetDayCheckboxes("G"),
        };
        _suppression.SetGlobalSchedule(sched);
    }

    // ── Per-type UI ────────────────────────────────────────────

    private AlertType? SelectedAlertType
    {
        get
        {
            if (AlertTypeCombo.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<AlertType>(tag, out var type))
                return type;
            return null;
        }
    }

    private void AlertTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedAlertType is null) return;

        var prev = _initializing;
        _initializing = true;
        try
        {
            PerTypePanel.Visibility = Visibility.Visible;
            PopulatePerTypeSchedule();
            UpdatePerTypeStatus();
        }
        finally
        {
            _initializing = prev;
        }
    }

    private void TypeSuppress10_Click(object sender, RoutedEventArgs e) => DoTypeSuppress(TimeSpan.FromMinutes(10));
    private void TypeSuppress30_Click(object sender, RoutedEventArgs e) => DoTypeSuppress(TimeSpan.FromMinutes(30));
    private void TypeSuppress60_Click(object sender, RoutedEventArgs e) => DoTypeSuppress(TimeSpan.FromHours(1));
    private void TypeSuppress120_Click(object sender, RoutedEventArgs e) => DoTypeSuppress(TimeSpan.FromHours(2));
    private void TypeSuppressIndefinite_Click(object sender, RoutedEventArgs e) => DoTypeSuppress(null);

    private void TypeClear_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAlertType is { } type)
        {
            _suppression.ClearTypeSuppression(type);
            UpdatePerTypeStatus();
            RefreshSummary();
        }
    }

    private void DoTypeSuppress(TimeSpan? duration)
    {
        if (SelectedAlertType is { } type)
        {
            _suppression.SuppressType(type, duration);
            UpdatePerTypeStatus();
            RefreshSummary();
        }
    }

    // ── Per-type schedule ──────────────────────────────────────

    private void TypeSchedule_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        TypeSchedulePanel.Visibility = TypeScheduleEnabled.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;

        SavePerTypeSchedule();
        RefreshSummary();
    }

    private void PopulatePerTypeSchedule()
    {
        var prev = _initializing;
        _initializing = true;
        try
        {
            var type = SelectedAlertType;
            if (type is null) return;

            var key = type.Value.ToString();
            if (_settings.Suppression.PerType.TryGetValue(key, out var rule) && rule.Schedule is { } sched)
            {
                TypeScheduleEnabled.IsChecked = sched.Enabled;
                TypeScheduleStart.Text = sched.StartTime;
                TypeScheduleEnd.Text = sched.EndTime;
                SetDayCheckboxes("T", sched.Days);
                TypeSchedulePanel.Visibility = sched.Enabled ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                TypeScheduleEnabled.IsChecked = false;
                TypeScheduleStart.Text = "22:00";
                TypeScheduleEnd.Text = "07:00";
                SetDayCheckboxes("T", new List<int>());
                TypeSchedulePanel.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            _initializing = prev;
        }
    }

    private void SavePerTypeSchedule()
    {
        if (SelectedAlertType is not { } type) return;

        var sched = new ScheduleSuppression
        {
            Enabled = TypeScheduleEnabled.IsChecked == true,
            StartTime = TypeScheduleStart.Text.Trim(),
            EndTime = TypeScheduleEnd.Text.Trim(),
            Days = GetDayCheckboxes("T"),
        };
        _suppression.SetTypeSchedule(type, sched);
    }

    // ── Status labels ──────────────────────────────────────────

    private void UpdateGlobalStatus()
    {
        GlobalStatusLabel.Text = AlertSuppressionService.IsRuleSuppressing(_settings.Suppression.Global)
            ? "⚠ Alerts are suppressed"
            : "No active suppression";
    }

    private void UpdatePerTypeStatus()
    {
        if (SelectedAlertType is not { } type) return;

        PerTypeStatusLabel.Text = _suppression.IsAlertSuppressed(type)
            ? "⚠ This alert type is suppressed"
            : "No active suppression for this type";
    }

    // ── Summary & helpers ──────────────────────────────────────

    private void RefreshSummary()
    {
        UpdateGlobalStatus();
        UpdatePerTypeStatus();

        var lines = _suppression.GetActiveSuppressionSummary();
        SummaryLabel.Text = lines.Count > 0
            ? string.Join("\n", lines)
            : "None";
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _suppression.ClearAll();
        RefreshSummary();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Day checkbox helpers ───────────────────────────────────

    private void SetDayCheckboxes(string prefix, List<int> days)
    {
        for (int i = 0; i < 7; i++)
        {
            var cb = FindName($"{prefix}Day{i}") as CheckBox;
            if (cb is not null)
                cb.IsChecked = days.Contains(i);
        }
    }

    private List<int> GetDayCheckboxes(string prefix)
    {
        var result = new List<int>();
        for (int i = 0; i < 7; i++)
        {
            var cb = FindName($"{prefix}Day{i}") as CheckBox;
            if (cb?.IsChecked == true)
                result.Add(i);
        }
        return result;
    }
}
