namespace Bread.Media;

public struct VideoFrameRate
{
    public int Num;
    public int Den;

    public double Value
    {
        get {
            if (Den == 0) return 0d;
            return Num / (double)Den;
        }
    }

    public int Ticks
    {
        get {
            if (Den == 0) return 0;
            return (int)(TimeSpan.TicksPerSecond / (double)Num * Den + 0.5);
        }
    }

    public VideoFrameRate(int num, int den)
    {
        Num = num;
        Den = den;
    }
}

public static class VideoFrameRateHelper
{
    public static VideoFrameRate ToVideoFrameRate(this double rate)
    {
        long ticks = (int)(TimeSpan.TicksPerSecond / rate + 0.5);
        if (Math.Abs(ticks - 166833) < 10) {
            return new VideoFrameRate(60000, 1001); //59.94
        }
        if (Math.Abs(ticks - 333667) < 10) {
            return new VideoFrameRate(30000, 1001); //29.97
        }
        if (Math.Abs(ticks - 417084) < 10) {
            return new VideoFrameRate(24000, 1001); //23.976
        }
        if (Math.Abs(ticks - 166667) < 10) {
            return new VideoFrameRate(60, 1); //60
        }
        if (Math.Abs(ticks - 333333) < 10) {
            return new VideoFrameRate(30, 1);
        }
        if (Math.Abs(ticks - 200000) < 10) {
            return new VideoFrameRate(50, 1);
        }
        if (Math.Abs(ticks - 400000) < 10) {
            return new VideoFrameRate(25, 1);
        }
        if (Math.Abs(ticks - 416667) < 10) {
            return new VideoFrameRate(24, 1);
        }
        return new VideoFrameRate((int)(rate + 0.5), 1);
    }
}

public enum VideoSampleFormat
{
    None,
    NV12,
    YV12,
    YUY2,
    RGB24,
    RGB32,
    ARGB32,
    H264,
    MJpeg
};


/// <summary>
/// 视频信息
/// </summary>
public struct VideoInfo
{
    /// <summary>
    /// 帧宽
    /// </summary>
    public int Width;

    /// <summary>
    /// 帧高
    /// </summary>
    public int Height;

    /// <summary>
    /// 帧率,与isNTSC组合使用
    /// </summary>
    public VideoFrameRate FrameRate;

    /// <summary>
    /// 比特率
    /// </summary>
    public Int64 BitRate;

    /// <summary>
    /// 视频帧的数据格式
    /// </summary>
    public VideoSampleFormat Format;
};


public static class VideoSampleForamtHelper
{
    public static int GetDefaultStride(this VideoSampleFormat format, int width)
    {
        switch (format) {
            case VideoSampleFormat.ARGB32:
            case VideoSampleFormat.RGB32:
                return width * 4;
            case VideoSampleFormat.RGB24:
                return (((width * 3 + 3) >> 2) << 2);
            case VideoSampleFormat.NV12:
            case VideoSampleFormat.YV12:
                return width;
            case VideoSampleFormat.YUY2:
                return width * 2;
        }
        return 0;
    }

    public static int GetDefaultPlaneHeight(this VideoSampleFormat format, int height, int index)
    {
        if (index == 1) {
            return height;
        }
        else if (index == 2) {
            if (format == VideoSampleFormat.NV12) return height / 2;
            else if (format == VideoSampleFormat.YV12) return height / 4;
        }
        else if (index == 3) {
            if (format == VideoSampleFormat.YV12) return height / 4;
        }
        return 0;
    }
}
