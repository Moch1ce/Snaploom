using Snaploom.Capture;

if (args is ["unsupported"])
{
    try
    {
        using var unsupported = new CaptureClient();
        return 20;
    }
    catch (PlatformNotSupportedException error) when (error.Message.Contains("SNAPLOOM001", StringComparison.Ordinal))
    {
        return 0;
    }
}

using var client = new CaptureClient();
if (client.RuntimeVersion.AbiMajor != 1 || !client.RuntimeVersion.NativeSemver.StartsWith("0.", StringComparison.Ordinal))
{
    return 21;
}

Console.WriteLine($"Snaploom.Capture native {client.RuntimeVersion.NativeSemver} ABI {client.RuntimeVersion.AbiMajor}");
return 0;
