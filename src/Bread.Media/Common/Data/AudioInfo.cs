using FFmpeg.AutoGen;

namespace Bread.Media;

public enum AudioSampleFormat : int
{
    None,
    Float32,
    SINT16,
    SINT32,
    AAC,
    MP3
};


/// <summary>
/// 音频信息
/// </summary>
public struct AudioInfo
{
    /// <summary>
    /// 采样率
    /// </summary>
    public int SampleRate;

    /// <summary>
    /// 通道数
    /// </summary>
    public int ChannelCount;

    /// <summary>
    /// sample 的数据格式
    /// </summary>
    public int BytesPerSample;

    /// <summary>
    /// 每秒的字节数
    /// </summary>
    public long BitRate;

    /// <summary>
    /// 音频采样点的数据格式
    /// </summary>
    public AudioSampleFormat Format;
};


public static class AudioSampleFormatHelper
{
    public static int GetBitsPerSample(this AudioSampleFormat format) => format switch {
        AudioSampleFormat.Float32 or AudioSampleFormat.SINT32 => 32,
        AudioSampleFormat.SINT16 => 16,
        _ => throw new InvalidOperationException($"{format} does not have a fixed-length sample"),
    };

    public static int GetBytesPerSample(this AudioSampleFormat format) => format switch {
        AudioSampleFormat.Float32 or AudioSampleFormat.SINT32 => 4,
        AudioSampleFormat.SINT16 => 2,
        _ => throw new InvalidOperationException($"{format} does not have a fixed-length sample"),
    };

    public static AVSampleFormat ToAVFormat(this AudioSampleFormat format) => format switch {
        AudioSampleFormat.Float32 => AVSampleFormat.AV_SAMPLE_FMT_FLT,
        AudioSampleFormat.SINT32 => AVSampleFormat.AV_SAMPLE_FMT_S32,
        AudioSampleFormat.SINT16 => AVSampleFormat.AV_SAMPLE_FMT_S16,
        _ => throw new InvalidOperationException($"{format} does not have a fixed-length sample"),
    };


    public static AudioSampleFormat ToAudioSampleFormat(this AVSampleFormat format) => format switch {
        AVSampleFormat.AV_SAMPLE_FMT_FLT => AudioSampleFormat.Float32,
        AVSampleFormat.AV_SAMPLE_FMT_S32 => AudioSampleFormat.SINT32,
        AVSampleFormat.AV_SAMPLE_FMT_S16 => AudioSampleFormat.SINT16,
        _ => throw new InvalidOperationException($"{format} does not have a fixed-length sample"),
    };
}
