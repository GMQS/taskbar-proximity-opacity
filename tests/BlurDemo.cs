using TaskbarProximityOpacity;
internal static class BlurDemo
{
    public static void Run()
    {
        using var background = new Form { Text = "Blur verification pattern", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(100, 150, 900, 400) };
        background.Paint += (_, e) => {
            for (int x = 0; x < 900; x += 12)
                e.Graphics.FillRectangle(x % 24 == 0 ? Brushes.White : Brushes.DarkBlue, x, 0, 12, 400);
            using var font = new Font("Segoe UI", 20);
            e.Graphics.DrawString("Background text should be blurred", font, Brushes.Red, 50, 180);
        };
        using var foreground = new Form { Text = "Blur verification taskbar", FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(100, 340, 900, 100), BackColor = Color.Black, Opacity = 0.25 };
        using var blur = new TaskbarBlur.BlurSurface();
        background.Shown += (_, _) => { foreground.Show(); blur.Place(foreground.Handle, foreground.Bounds); Console.WriteLine("Acrylic API accepted: " + blur.EffectAvailable); };
        using var timer = new System.Windows.Forms.Timer { Interval = 120000 };
        timer.Tick += (_, _) => background.Close();
        timer.Start();
        Application.Run(background);
    }
}
