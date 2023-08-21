namespace Bread.Media;

public class EndOfStreamException : Exception
{
    public EndOfStreamException(string msg = "") : base(msg) { }
}
