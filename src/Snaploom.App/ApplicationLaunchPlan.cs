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
    private ApplicationLaunchPlan(bool showTrayIcon, IReadOnlyList<TrayItemPlan> trayItems)
    {
        ShowTrayIcon = showTrayIcon;
        TrayItems = trayItems;
    }

    public bool ShowTrayIcon { get; }

    public IReadOnlyList<TrayItemPlan> TrayItems { get; }

    public static ApplicationLaunchPlan Create(bool canStartScreenshot)
    {
        ReadOnlyCollection<TrayItemPlan> trayItems = Array.AsReadOnly(
        [
            new TrayItemPlan(TrayAction.StartScreenshot, IsEnabled: canStartScreenshot),
            new TrayItemPlan(TrayAction.Exit, IsEnabled: true),
        ]);

        return new ApplicationLaunchPlan(showTrayIcon: true, trayItems);
    }
}
