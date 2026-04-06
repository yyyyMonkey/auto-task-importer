using System.Net;

namespace M365TfsSync.Services;

public class AuthException : Exception
{
    public AuthException(string message) : base(message) { }
    public AuthException(string message, Exception inner) : base(message, inner) { }
}

public class GraphApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ErrorDescription { get; }

    public GraphApiException(HttpStatusCode statusCode, string errorDescription)
        : base($"Graph API 錯誤 [{(int)statusCode}]: {errorDescription}")
    {
        StatusCode = statusCode;
        ErrorDescription = errorDescription;
    }
}

public class TfsApiException : Exception
{
    public string ErrorDescription { get; }

    public TfsApiException(string errorDescription)
        : base($"TFS API 錯誤: {errorDescription}")
    {
        ErrorDescription = errorDescription;
    }

    public TfsApiException(string errorDescription, Exception inner)
        : base($"TFS API 錯誤: {errorDescription}", inner)
    {
        ErrorDescription = errorDescription;
    }
}

public class SettingsException : Exception
{
    public SettingsException(string message) : base(message) { }
    public SettingsException(string message, Exception inner) : base(message, inner) { }
}
