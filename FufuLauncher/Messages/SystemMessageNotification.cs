using Windows.UI;

namespace FufuLauncher.Messages;

public class SystemMessageNotification
{
    public string Message { get; }
    public string IconGlyph { get; }
    public Color IconColor { get; }

    public SystemMessageNotification(string message, string iconGlyph, Color iconColor)
    {
        Message = message;
        IconGlyph = iconGlyph;
        IconColor = iconColor;
    }
}