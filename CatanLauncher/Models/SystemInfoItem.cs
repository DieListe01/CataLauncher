using System.Windows;

namespace CatanLauncher.Models;

public sealed class SystemInfoItem
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public string BadgeKind { get; set; } = "neutral";
    public string ActionId { get; set; } = string.Empty;
    public string ActionText { get; set; } = string.Empty;
    public string ActionToolTip { get; set; } = string.Empty;
    public Visibility BadgeVisibility => string.IsNullOrWhiteSpace(BadgeText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ActionVisibility => string.IsNullOrWhiteSpace(ActionId) || string.IsNullOrWhiteSpace(ActionText) ? Visibility.Collapsed : Visibility.Visible;
}
