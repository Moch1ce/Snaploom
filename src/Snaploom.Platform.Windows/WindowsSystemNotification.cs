using Snaploom.Core;

#if WINDOWS
using Windows.UI.Notifications;
#endif

namespace Snaploom.Platform.Windows;

internal static class WindowsSystemNotification
{
    internal static void Show(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
#if WINDOWS
        var document = ToastNotificationManager.GetTemplateContent(
            ToastTemplateType.ToastText02);
        var textNodes = document.GetElementsByTagName("text");
        textNodes[0].AppendChild(document.CreateTextNode(title));
        textNodes[1].AppendChild(document.CreateTextNode(message));
        var notification = new ToastNotification(document);
        ToastNotificationManager.CreateToastNotifier(ProductIdentity.WindowsAppId)
            .Show(notification);
#else
        throw new PlatformNotSupportedException(
            "Windows notifications require a Windows target framework.");
#endif
    }
}
