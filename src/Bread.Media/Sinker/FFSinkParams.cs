namespace Bread.Media;

internal class FFSinkParams
{
    public VideoEncodeParams Video { get; set; }

    public AudioEncodeParams Audio { get; set; }

    public FFSinkParams(VideoEncodeParams video, AudioEncodeParams audio)
    {
        Video = video;
        Audio = audio;
    }
}
