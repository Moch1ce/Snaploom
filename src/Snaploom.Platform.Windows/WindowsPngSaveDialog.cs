using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Snaploom.Platform.Windows;

internal static partial class WindowsPngSaveDialog
{
    private const int MaximumPathLength = 32768;
    private const uint OfnOverwritePrompt = 0x00000002;
    private const uint OfnNoChangeDirectory = 0x00000008;
    private const uint OfnPathMustExist = 0x00000800;
    private const uint OfnExplorer = 0x00080000;
    private const uint OfnDoNotAddToRecent = 0x02000000;

    internal static string? Show(string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        if (suggestedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The suggested file name contains invalid characters.", nameof(suggestedFileName));
        }

        var fileBuffer = Marshal.AllocHGlobal(MaximumPathLength * sizeof(char));
        var filter = Marshal.StringToCoTaskMemUni("PNG image (*.png)\0*.png\0\0");
        var title = Marshal.StringToCoTaskMemUni("保存截图");
        var defaultExtension = Marshal.StringToCoTaskMemUni("png");
        try
        {
            Span<char> emptyBuffer;
            unsafe
            {
                emptyBuffer = new Span<char>((void*)fileBuffer, MaximumPathLength);
            }

            emptyBuffer.Clear();
            suggestedFileName.AsSpan().CopyTo(emptyBuffer);

            var dialog = new OpenFileName
            {
                Size = (uint)Marshal.SizeOf<OpenFileName>(),
                Filter = filter,
                FilterIndex = 1,
                File = fileBuffer,
                MaximumFileLength = MaximumPathLength,
                Title = title,
                Flags = OfnOverwritePrompt |
                        OfnNoChangeDirectory |
                        OfnPathMustExist |
                        OfnExplorer |
                        OfnDoNotAddToRecent,
                DefaultExtension = defaultExtension,
            };

            if (GetSaveFileName(ref dialog) != 0)
            {
                return Marshal.PtrToStringUni(fileBuffer);
            }

            var error = CommDlgExtendedError();
            if (error == 0)
            {
                return null;
            }

            throw new Win32Exception(checked((int)error), $"The Windows save dialog failed with code 0x{error:X8}.");
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
            Marshal.FreeCoTaskMem(filter);
            Marshal.FreeCoTaskMem(title);
            Marshal.FreeCoTaskMem(defaultExtension);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        internal uint Size;
        internal nint Owner;
        internal nint Instance;
        internal nint Filter;
        internal nint CustomFilter;
        internal uint MaximumCustomFilterLength;
        internal uint FilterIndex;
        internal nint File;
        internal uint MaximumFileLength;
        internal nint FileTitle;
        internal uint MaximumFileTitleLength;
        internal nint InitialDirectory;
        internal nint Title;
        internal uint Flags;
        internal ushort FileOffset;
        internal ushort FileExtension;
        internal nint DefaultExtension;
        internal nint CustomData;
        internal nint Hook;
        internal nint TemplateName;
        internal nint Reserved;
        internal uint ReservedValue;
        internal uint ExtendedFlags;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", SetLastError = true)]
    private static partial int GetSaveFileName(ref OpenFileName dialog);

    [LibraryImport("comdlg32.dll")]
    private static partial uint CommDlgExtendedError();
}
