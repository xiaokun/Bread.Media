namespace Bread.Media;

public enum ImageFileFormat : int
{
    None = 0,
    JPG,
    PNG,
    JPEG,
    BMP,
    GIF
}

public static class ImageFileFormatHelper
{
    public static string GetExtension(this ImageFileFormat format)
    {
        if (format == ImageFileFormat.None) return "";
        return $".{format.ToString().ToLower()}";
    }

    public static List<string> GetExtensions()
    {
        var list = new List<string>();
        list.Add(".jpg");
        list.Add(".png");
        list.Add(".jpeg");
        list.Add(".bmp");
        list.Add(".gif");
        return list;
    }

    public static string GetFileFilter()
    {
        return "图片文件|*.jpg;*.png;*.jpeg;*.bmp;*.gif";
    }

    public static bool IsImageFile(this string ext)
    {
        var format = GetImageFileFormat(ext);
        if (format == ImageFileFormat.None) return false;
        return true;
    }

    public static ImageFileFormat GetImageFileFormat(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return ImageFileFormat.None;
        ext = ext.ToLower();
        switch (ext) {
            case ".png":
                return ImageFileFormat.PNG;
            case ".jpg":
                return ImageFileFormat.JPG;
            case ".jpeg":
                return ImageFileFormat.JPG;
            case ".bmp":
                return ImageFileFormat.BMP;
            case ".gif":
                return ImageFileFormat.GIF;
        }
        return ImageFileFormat.None;
    }
}


public enum VideoFileFormat : int
{
    None = 0,
    MP4,
    MOV,
    MKV
}

public static class VideoFileFormatHelper
{
    public static string GetExtension(this VideoFileFormat format)
    {
        if (format == VideoFileFormat.None) return "";
        return $".{format.ToString().ToLower()}";
    }

    public static bool IsVideoFile(this string ext)
    {
        var format = GetVideoFileFormat(ext);
        if (format == VideoFileFormat.None) return false;
        return true;
    }

    public static string GetFileFilter()
    {
        return "视频文件|*.mp4;*.mov;*.mkv";
    }

    public static VideoFileFormat GetVideoFileFormat(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return VideoFileFormat.None;
        ext = ext.ToLower();
        return ext switch {
            ".mp4" => VideoFileFormat.MP4,
            ".mkv" => VideoFileFormat.MKV,
            ".mov" => VideoFileFormat.MOV,
            _ => VideoFileFormat.None,
        };
    }
}


public enum AudioFileFormat : int
{
    None = 0,
    MP3,
    WAV,
    AAC,
    APE,
    FLAC
}

public static class AudioFileFormatHelper
{
    public static string GetExtension(this AudioFileFormat format)
    {
        if (format == AudioFileFormat.None) return "";
        return $".{format.ToString().ToLower()}";
    }

    public static bool IsAudioFile(this string ext)
    {
        var format = GetAudioFileFormat(ext);
        if (format == AudioFileFormat.None) return false;
        return true;
    }

    public static string GetFileFilter()
    {
        return "音频文件|*.mp3;*.wav;*.aac;*.ape;*.flac";
    }

    public static AudioFileFormat GetAudioFileFormat(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return AudioFileFormat.None;
        ext = ext.ToLower();
        switch (ext) {
            case ".mp3":
                return AudioFileFormat.MP3;
            case ".wav":
                return AudioFileFormat.WAV;
            case ".aac":
                return AudioFileFormat.AAC;
            case ".ape":
                return AudioFileFormat.APE;
            case ".flac":
                return AudioFileFormat.FLAC;
        }
        return AudioFileFormat.None;
    }
}
