using System.Drawing;

namespace Bread.Media;

public class Effect
{
    public Rectangle Src { get; set; } = Rectangle.Empty;

    public ColorKey ColorKey { get; set; } = ColorKey.Empty;

    public Rectangle Dst { get; set; } = Rectangle.Empty;

    public double Alpha { get; set; } = 1.0;

    public bool IsVisiable { get; set; } = false;
}
