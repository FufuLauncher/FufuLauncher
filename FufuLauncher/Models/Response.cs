/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
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
