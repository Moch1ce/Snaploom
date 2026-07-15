using System.Text;
using System.Security;

namespace Snaploom.App;

public enum AppLogEvent
{
    ApplicationStarted,
    ApplicationStopped,
    PermissionMissing,
    CaptureFailed,
    UnexpectedCaptureFailure,
    HotKeyConflict,
    HotKeyReregisterFailed,
    SaveFailed,
    ClipboardFailed,
    AutoStartFailed,
    PlatformUnavailable,
    LogsCleared,
}

public sealed class PrivacyLog
{
    public const int MaxFileCount = 5;
    public const int MaxFileBytes = 2 * 1024 * 1024;

    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false);
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly int _maxFileBytes;

    public PrivacyLog(string directory, int maxFileBytes = MaxFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 128);
        _directory = directory;
        _maxFileBytes = maxFileBytes;
    }

    public string DirectoryPath => _directory;

    public void Info(AppLogEvent logEvent) => TryWrite("INFO", logEvent, exception: null);

    public void Error(AppLogEvent logEvent, Exception? exception = null) =>
        TryWrite("ERROR", logEvent, exception);

    public bool Clear()
    {
        try
        {
            lock (_sync)
            {
                if (!Directory.Exists(_directory))
                {
                    return true;
                }

                foreach (var path in Directory.EnumerateFiles(_directory, "snaploom*.log"))
                {
                    File.Delete(path);
                }

                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    public static string GetDefaultDirectory()
    {
        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(applicationData))
        {
            applicationData = AppContext.BaseDirectory;
        }

        return Path.Combine(applicationData, "Snaploom", "Logs");
    }

    private void TryWrite(string level, AppLogEvent logEvent, Exception? exception)
    {
        try
        {
            Write(level, logEvent, exception);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SecurityException)
        {
        }
    }

    private void Write(string level, AppLogEvent logEvent, Exception? exception)
    {
        var exceptionType = exception?.GetType().Name;
        var line = exceptionType is null
            ? $"{DateTimeOffset.UtcNow:O} {level} {logEvent}{Environment.NewLine}"
            : $"{DateTimeOffset.UtcNow:O} {level} {logEvent} type={exceptionType}{Environment.NewLine}";
        var lineBytes = Utf8WithoutBom.GetByteCount(line);
        lock (_sync)
        {
            Directory.CreateDirectory(_directory);
            DeleteOversizedFiles();
            var currentPath = GetLogPath(index: 0);
            if (File.Exists(currentPath) &&
                new FileInfo(currentPath).Length + lineBytes > _maxFileBytes)
            {
                Rotate();
            }

            File.AppendAllText(currentPath, line, Utf8WithoutBom);
        }
    }

    private void Rotate()
    {
        File.Delete(GetLogPath(MaxFileCount - 1));
        for (var index = MaxFileCount - 1; index > 0; index--)
        {
            var source = GetLogPath(index - 1);
            if (File.Exists(source))
            {
                File.Move(source, GetLogPath(index), overwrite: true);
            }
        }
    }

    private void DeleteOversizedFiles()
    {
        for (var index = 0; index < MaxFileCount; index++)
        {
            var path = GetLogPath(index);
            if (File.Exists(path) && new FileInfo(path).Length > _maxFileBytes)
            {
                File.Delete(path);
            }
        }
    }

    private string GetLogPath(int index) => Path.Combine(
        _directory,
        index == 0 ? "snaploom.log" : $"snaploom.{index}.log");
}
