namespace CareHR.RfidGateway.UI;

internal static class AppBranding
{
    private static Icon? _appIcon;
    private static Image? _logoImage;

    public static Icon AppIcon => _appIcon ??= LoadIcon();

    public static Image LogoImage => _logoImage ??= LoadLogoImage();

    public static void ApplyFormIcon(Form form)
    {
        form.Icon = AppIcon;
    }

    public static PictureBox CreateLogoPictureBox(int height = 40)
    {
        var image = LogoImage;
        var width = Math.Max(1, (int)Math.Round(height * (image.Width / (double)image.Height)));
        return new PictureBox
        {
            Image = image,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(width, height),
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private static Icon LoadIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "carehr-logo.ico");
        if (File.Exists(icoPath))
        {
            return new Icon(icoPath);
        }

        // Fallback embedded next to exe root (copy-to-output link)
        var alt = Path.Combine(AppContext.BaseDirectory, "carehr-logo.ico");
        if (File.Exists(alt))
        {
            return new Icon(alt);
        }

        return SystemIcons.Application;
    }

    private static Image LoadLogoImage()
    {
        var pngPath = Path.Combine(AppContext.BaseDirectory, "Assets", "carehr-logo.png");
        if (File.Exists(pngPath))
        {
            return Image.FromFile(pngPath);
        }

        var alt = Path.Combine(AppContext.BaseDirectory, "carehr-logo.png");
        if (File.Exists(alt))
        {
            return Image.FromFile(alt);
        }

        return SystemIcons.Application.ToBitmap();
    }
}
