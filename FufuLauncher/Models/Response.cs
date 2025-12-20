namespace FufuLauncher.Models;

public class Response<T>
{
    public int Retcode
    {
        get; set;
    }
    public string Message { get; set; } = string.Empty;
    public T? Data
    {
        get; set;
    }
}