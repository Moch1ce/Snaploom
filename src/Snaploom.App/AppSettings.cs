using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

public enum AppLanguage
{
    System,
    SimplifiedChinese,
    English,
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public ScreenshotHotKeyModifiers HotKeyModifiers { get; init; }

    public ScreenshotHotKeyKey HotKeyKey { get; init; }

    public bool AutoStart { get; init; }

    public ScreenshotAnnotationColor AnnotationColor { get; init; }

    public int AnnotationLineWidth { get; init; }

    public ScreenshotAnnotationColor TextColor { get; init; }

    public int TextFontSize { get; init; }

    public int MosaicBrushSize { get; init; }

    public string? LastSaveDirectory { get; init; }

    public AppLanguage Language { get; init; }

    public AppTheme Theme { get; init; }

    [JsonIgnore]
    public ScreenshotHotKey HotKey => new(HotKeyModifiers, HotKeyKey);

    [JsonIgnore]
    public ScreenshotAnnotationStyle AnnotationStyle =>
        new(AnnotationColor, AnnotationLineWidth);

    [JsonIgnore]
    public ScreenshotTextStyle TextStyle => new(TextColor, TextFontSize);

    [JsonIgnore]
    public ScreenshotMosaicStyle MosaicStyle => new(
        MosaicBrushSize,
        MosaicBrushSize switch
        {
            16 => 8,
            32 => 12,
            64 => 16,
            _ => throw new InvalidOperationException("The mosaic preference is invalid."),
        });

    public static AppSettings CreateDefault(DesktopPlatformKind platform)
    {
        var hotKey = ScreenshotHotKeyDefaults.For(platform);
        return new AppSettings
        {
            HotKeyModifiers = hotKey.Modifiers,
            HotKeyKey = hotKey.Key,
            AutoStart = false,
            AnnotationColor = ScreenshotAnnotationStyle.Default.Color,
            AnnotationLineWidth = ScreenshotAnnotationStyle.Default.LineWidth,
            TextColor = ScreenshotTextStyle.Default.Color,
            TextFontSize = ScreenshotTextStyle.Default.FontSize,
            MosaicBrushSize = ScreenshotMosaicStyle.Default.BrushSize,
            LastSaveDirectory = null,
            Language = AppLanguage.System,
            Theme = AppTheme.System,
        };
    }

    internal bool IsValid()
    {
        const ScreenshotHotKeyModifiers allModifiers =
            ScreenshotHotKeyModifiers.Alt |
            ScreenshotHotKeyModifiers.Control |
            ScreenshotHotKeyModifiers.Shift |
            ScreenshotHotKeyModifiers.Command;
        return SchemaVersion == CurrentSchemaVersion &&
               HotKeyModifiers != ScreenshotHotKeyModifiers.None &&
               (HotKeyModifiers & ~allModifiers) == 0 &&
               Enum.IsDefined(HotKeyKey) &&
               Enum.IsDefined(AnnotationColor) &&
               AnnotationLineWidth is 2 or 4 or 8 &&
               Enum.IsDefined(TextColor) &&
               TextFontSize is 16 or 24 or 32 &&
               MosaicBrushSize is 16 or 32 or 64 &&
               Enum.IsDefined(Language) &&
               Enum.IsDefined(Theme);
    }
}

public sealed class JsonAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly AppSettings _defaults;

    public JsonAppSettingsStore(string path, AppSettings defaults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(defaults);
        _path = path;
        _defaults = defaults;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return _defaults;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return settings?.IsValid() == true ? settings : _defaults;
        }
        catch (JsonException)
        {
            return _defaults;
        }
        catch (IOException)
        {
            return _defaults;
        }
        catch (UnauthorizedAccessException)
        {
            return _defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid())
        {
            throw new ArgumentException("Cannot persist invalid Snaploom settings.", nameof(settings));
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_path}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}

public sealed class AppSettingsService
{
    private readonly JsonAppSettingsStore? _store;

    public AppSettingsService(JsonAppSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Current = store.Load();
    }

    private AppSettingsService(AppSettings initialSettings)
    {
        Current = initialSettings;
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public static AppSettingsService CreateTransient(AppSettings initialSettings)
    {
        ArgumentNullException.ThrowIfNull(initialSettings);
        return new AppSettingsService(initialSettings);
    }

    public void Update(Func<AppSettings, AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var next = update(Current);
        if (!next.IsValid())
        {
            throw new ArgumentException("The updated Snaploom settings are invalid.", nameof(update));
        }

        if (next == Current)
        {
            return;
        }

        try
        {
            _store?.Save(next);
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

        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string GetDefaultPath()
    {
        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(applicationData))
        {
            applicationData = AppContext.BaseDirectory;
        }

        return Path.Combine(applicationData, "Snaploom", "settings.json");
    }
}
