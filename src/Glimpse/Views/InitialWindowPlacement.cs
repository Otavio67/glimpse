using System.Windows;
namespace Glimpse.Views;

public static class InitialWindowPlacement
{
    // WPF work areas and window coordinates are in device-independent pixels.
    public static Rect FitInside(Rect workArea)
    {
        if (workArea.IsEmpty || workArea.Width <= 0 || workArea.Height <= 0)
            return new Rect(0, 0, 900, 650);
        double marginX = Math.Min(16, workArea.Width / 20);
        double marginY = Math.Min(16, workArea.Height / 20);
        double width = Math.Min(1390, Math.Max(1, workArea.Width - 2 * marginX));
        double height = Math.Min(880, Math.Max(1, workArea.Height - 2 * marginY));
        return new Rect(workArea.Left + (workArea.Width - width) / 2,
                        workArea.Top + (workArea.Height - height) / 2, width, height);
    }
}
