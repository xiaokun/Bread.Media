using FFmpeg.AutoGen;

namespace Bread.Media;

public unsafe class PacketSample : Sample
{
    public bool IsAudio { get; private set; }

    AVPacket* Packet = null;

    public PacketSample(AVPacket* pkt, bool isAudio = true)
    {
        IsAudio = isAudio;
        Packet = ffmpeg.av_packet_clone(pkt);
    }

    public AVPacket* GetPointer()
    {
        return Packet;
    }

    protected override void Dispose(bool disposeManageBuffer)
    {
        base.Dispose(disposeManageBuffer);

        if (Packet != null) {
            var pkt = Packet;
            ffmpeg.av_packet_free(&pkt);
            Packet = null;
        }

    }
}
