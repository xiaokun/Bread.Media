using System.IO;
using System.Reflection;
using FFmpeg.AutoGen;


namespace Bread.Media;

internal class Aspects
{
    public static readonly string FFmpeg = "ffmpeg";
}

/// <summary>
/// Defaults and constants of the Media Engine.
/// </summary>
internal static partial class Constants
{
    /// <summary>
    /// Initializes static members of the <see cref="Constants"/> class.
    /// </summary>
    static Constants()
    {
        try {
            var path = Assembly.GetEntryAssembly()?.Location;
            var entryAssemblyPath = Path.GetDirectoryName(path) ?? ".";
            FFmpegSearchPath = Path.GetFullPath(entryAssemblyPath);
            return;
        }
        catch {
            // ignore (we might be in winforms design time)
            // see issue #311
        }

        FFmpegSearchPath = ffmpeg.RootPath;
    }

    /// <summary>
    /// Gets the assembly location.
    /// </summary>
    public static string FFmpegSearchPath { get; }

    /// <summary>
    /// The default volume.
    /// </summary>
    public static double DefaultVolume => 1.0d;

    /// <summary>
    /// The maximum volume.
    /// </summary>
    public static double MaxVolume => 1.0d;

    /// <summary>
    /// The minimum volume.
    /// </summary>
    public static double MinVolume => 0.0d;

    /// <summary>
    /// Numbers of samples per channel per AudioSample
    /// </summary>
    public static int AudioSampleLength => 1024;

    /// <summary>
    /// The audio sample format.
    /// </summary>
    public static AVSampleFormat AudioSampleFormat => AVSampleFormat.AV_SAMPLE_FMT_FLT;

    /// <summary>
    /// The audio channel count.
    /// </summary>
    public static int AudioChannelCount => 2;

    /// <summary>
    /// The audio sample rate (per channel).
    /// </summary>
    public static int AudioSampleRate => 48000;

    /// <summary>
    /// The video pixel format. BGRA, 32bit.
    /// </summary>
    public static AVPixelFormat VideoPixelFormat => AVPixelFormat.AV_PIX_FMT_BGRA;

    /// <summary>
    /// Gets the timing period for default scenarios.
    /// </summary>
    internal static TimeSpan DefaultTimingPeriod => TimeSpan.FromMilliseconds(15);


    internal static int VideoStreamTimebase => 90000;

    /// <summary>
    /// The maximum video cache count : 3
    /// </summary>
    internal static int VideoCacheCount => 6;

    /// <summary>
    /// The maximum audio cache count : 6
    /// </summary>
    internal static int AudioCacheCount => 12;

    /// <summary>
    /// The default audio encoder id: AV_CODEC_ID_AAC
    /// </summary>
    internal static AVCodecID AudioCodeId => AVCodecID.AV_CODEC_ID_AAC;

}
