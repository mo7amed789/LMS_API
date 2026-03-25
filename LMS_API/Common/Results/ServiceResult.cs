namespace LMS_API.Common.Results;

public class ServiceResult<T>
{
    public bool IsSuccess { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }

    public static ServiceResult<T> Success(T data, string? message = null)
        => new()
        {
            IsSuccess = true,
            Data = data,
            Message = message
        };

    public static ServiceResult<T> Failure(string message)
        => new()
        {
            IsSuccess = false,
            Message = message
        };
}
