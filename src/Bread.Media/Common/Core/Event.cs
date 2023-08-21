namespace Bread.Media;

public interface ISender
{
    /// <summary>
    /// add <see cref="IListener"/>
    /// </summary>
    /// <param name="listener">listener</param>
    /// <param name="id">event id</param>
    void ConnectTo(IListener listener);

    void Disconnect(IListener listener);

    /// <summary>
    /// send data to all <see cref="IListener"/>
    /// </summary>
    /// <param name="e"></param>
    /// <returns>true if any accepted, otherwise false. Usually used as a speed control feedback parameter.</returns>
    bool Post(object data);
}


public interface IListener
{
    /// <summary>
    /// 接收 <see cref="ISender"/> 发送的数据
    /// </summary>
    /// <param name="data"></param>
    /// <returns>true if accepted, otherwise false.</returns>
    bool QueueEvent(object data);
}
