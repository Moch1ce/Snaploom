using System.Collections.ObjectModel;

namespace Snaploom.App;

public enum TrayAction
{
    StartScreenshot,
    Exit,
}

public sealed record TrayItemPlan(TrayAction Action, bool IsEnabled);

public sealed class ApplicationLaunchPlan
{
    private static readonly ReadOnlyCollection<TrayItemPlan> DefaultTrayItems =
        Array.AsReadOnly(
        [
            new TrayItemPlan(TrayAction.StartScreenshot, IsEnabled: false),
            new TrayItemPlan(TrayAction.Exit, IsEnabled: true),
        ]);

    private ApplicationLaunchPlan(bool showTrayIcon, IReadOnlyList<TrayItemPlan> trayItems)
    {
        ShowTrayIcon = showTrayIcon;
        TrayItems = trayItems;
    }

    public bool ShowTrayIcon { get; }

    public IReadOnlyList<TrayItemPlan> TrayItems { get; }

    public static ApplicationLaunchPlan Default { get; } = new(
        showTrayIcon: true,
        trayItems: DefaultTrayItems);
}
