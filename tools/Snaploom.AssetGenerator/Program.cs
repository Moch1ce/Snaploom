using Snaploom.Rendering;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Snaploom.AssetGenerator <windows-icon-output-path>");
    return 2;
}

var outputPath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

File.WriteAllBytes(outputPath, TrayIconRenderer.RenderWindowsIco());
Console.WriteLine($"Generated {Path.GetFileName(outputPath)}.");
return 0;
