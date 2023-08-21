using System.Drawing;

namespace Bread.Media;

public class ColorKey
{
    public static ColorKey Empty { get; } = new ColorKey(0, 0, 0, 0, 0);

    /// <summary>
    /// 色键颜色值
    /// </summary>
    public Color KeyColor { get; set; }

    /// <summary>
    /// 阈值低值
    /// </summary>
    public int ThresholdLow { get; set; } = 10;

    /// <summary>
    /// 阈值高值
    /// </summary>
    public int ThresholdHight { get; set; } = 32;

    /// <summary>
    /// 边缘羽化
    /// </summary>
    public int FadeFactor { get; set; } = 50;

    /// <summary>
    /// 羽化系数
    /// </summary>
    public int FadeRange { get; set; } = 50;

    public ColorKey()
    {

    }

    public ColorKey(int color, int low, int hight, int factor, int range)
    {
        KeyColor = Color.FromArgb(color);
        ThresholdLow = low;
        ThresholdHight = hight;
        FadeFactor = factor;
        FadeRange = range;
    }
}
