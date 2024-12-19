namespace Zenith.Raknet.Log;

public interface ILogger
{
    public void Clear() => Console.Clear();

    public void Debug(string message);

    public void Info(string message);

    public void Warning(string message);

    public void Error(string message);
}