using System.Collections.ObjectModel;

namespace Snaploom.App;

public enum TrayAction
{
    StartScreenshot,
    Exit,
}

public sealed record TrayItemPlan(TrayAction Action, bool IsEnabled);

public sealed record ApplicationLaunchPlan(
    bool ShowMainWindow,
    bool ShowTrayIcon,
    IReadOnlyList<TrayItemPlan> TrayItems)
{
    private static readonly ReadOnlyCollection<TrayItemPlan> DefaultTrayItems =
        Array.AsReadOnly(
        [
            new TrayItemPlan(TrayAction.StartScreenshot, IsEnabled: false),
            new TrayItemPlan(TrayAction.Exit, IsEnabled: true),
        ]);

    public static ApplicationLaunchPlan Default { get; } = new(
        ShowMainWindow: false,
        ShowTrayIcon: true,
        TrayItems: DefaultTrayItems);
}
