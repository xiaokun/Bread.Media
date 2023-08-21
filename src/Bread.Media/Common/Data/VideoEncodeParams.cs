using FFmpeg.AutoGen;

namespace Bread.Media;

public class VideoEncodeParams
{
    /// <summary>
    /// 帧宽
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 帧高
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 帧率,与isNTSC组合使用
    /// </summary>
    public VideoFrameRate FrameRate { get; set; }

    /// <summary>
    /// 比特率
    /// </summary>
    public Int64 BitRate { get; set; }

    /// <summary>
    /// ffmpeg codec name<br/>
    /// libx264rgb, h264_amf, h264_nvenc, h264_qsv, mjpeg, mjpeg_qsv, libx265, dnxhd
    /// </summary>
    public string CodecName { get; set; } = "libx265";


    public AVCodecID CodeId { get; set; } = AVCodecID.AV_CODEC_ID_NONE;

    /// <summary>
    /// Encode profile
    /// </summary>
    public int Profile { get; set; } = 100;

    /// <summary>
    /// Encode level
    /// </summary>
    public int Level { get; set; } = 51;

    /// <summary>
    /// The pps & sps data
    /// </summary>
    public IntPtr PPSSPSBufer { get; set; } = IntPtr.Zero;

    /// <summary>
    /// The length of the pps & sps data buffer
    /// </summary>
    public int BufferLength { get; set; } = 0;


    /// <summary>
    /// 压缩等级， 0~12，数值越高损失越小。
    /// </summary>
    public int CompressionLevel { get; set; } = 12;

    public VideoEncodeParams()
    {
    }

    public VideoEncodeParams(VideoInfo info)
    {
        Width = info.Width;
        Height = info.Height;
        FrameRate = info.FrameRate;
        BitRate = info.BitRate;
    }
}
