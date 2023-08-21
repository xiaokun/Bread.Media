namespace Bread.Media;

public interface IProcessor : IDisposable
{
    bool Process(Sample sample);
}
