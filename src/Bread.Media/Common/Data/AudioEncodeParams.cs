using FFmpeg.AutoGen;

namespace Bread.Media;

public class AudioEncodeParams
{
    public AVCodecID CodeId { get; set; } = Constants.AudioCodeId;

    /// <summary>
    /// 采样率
    /// </summary>
    public int SampleRate { get; set; }

    /// <summary>
    /// 通道数
    /// </summary>
    public int ChannelCount { get; set; }

    /// <summary>
    /// 每秒的字节数
    /// </summary>
    public long BitRate { get; set; }

    /// <summary>
    /// 压缩等级， 0~12，数值越高损失越小。
    /// </summary>
    public int CompressionLevel { get; set; } = 12;

    public AudioEncodeParams()
    {
    }

    public AudioEncodeParams(AudioInfo info)
    {
        SampleRate = info.SampleRate;
        ChannelCount = info.ChannelCount;
        BitRate = info.BitRate;
    }
}
