using Snaploom.Rendering;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Snaploom.AssetGenerator windows-icon <output-path>");
    Console.Error.WriteLine("   or: Snaploom.AssetGenerator macos-iconset <output-directory>");
    return 2;
}

switch (args[0])
{
    case "windows-icon":
    {
        var outputPath = Path.GetFullPath(args[1]);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        File.WriteAllBytes(outputPath, TrayIconRenderer.RenderWindowsIco());
        Console.WriteLine($"Generated {Path.GetFileName(outputPath)}.");
        return 0;
    }
    case "macos-iconset":
    {
        var outputDirectory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(outputDirectory);
        var iconset = TrayIconRenderer.RenderMacOSIconset();
        foreach (var (fileName, png) in iconset)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
        }

        Console.WriteLine($"Generated {iconset.Count} macOS icon assets.");
        return 0;
    }
    default:
        Console.Error.WriteLine($"Unknown asset kind: {args[0]}");
        return 2;
}
