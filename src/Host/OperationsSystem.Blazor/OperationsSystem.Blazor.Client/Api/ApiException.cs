namespace OperationsSystem.Blazor.Client.Api;

public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string responseBody)
        : base($"API request failed with status code {statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public string ResponseBody { get; }
}
