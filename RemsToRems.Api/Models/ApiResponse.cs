namespace RemsToRems.Api.Models;

public sealed class ApiResponse<T>
{
    public bool Status { get; init; }

    public T? Data { get; init; }

    public string Message { get; init; } = string.Empty;

    public static ApiResponse<T> Success(T data, string message = "OK")
    {
        return new ApiResponse<T>
        {
            Status = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<T> Error(string message)
    {
        return new ApiResponse<T>
        {
            Status = false,
            Message = message
        };
    }
}
