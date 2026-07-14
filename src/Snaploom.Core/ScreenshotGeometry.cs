namespace Snaploom.Core;

public readonly record struct PhysicalPoint(int X, int Y);

public readonly record struct PhysicalSize
{
    public PhysicalSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}

public readonly record struct LogicalSize
{
    public LogicalSize(double width, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }
}

public readonly record struct PhysicalRect(int X, int Y, int Width, int Height)
{
    public static PhysicalRect FromPoints(PhysicalPoint first, PhysicalPoint second) =>
        new(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(second.X - first.X),
            Math.Abs(second.Y - first.Y));
}
