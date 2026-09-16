namespace LlmServy.Services;

/// <summary>Stable message identifier and immutable formatting parameters, independent of UI culture.</summary>
public sealed record AppMessage
{
    public string Code
    {
        get;
    }
    public IReadOnlyList<object> Arguments
    {
        get;
    }
    public AppMessage(string code, params object[] arguments)
    {
        Code = code;
        Arguments = Array.AsReadOnly((object[])arguments.Clone());
    }
}

public sealed class AppException(AppMessage detail, Exception? inner = null) : Exception(detail.Code, inner)
{
    public AppMessage Detail { get; } = detail;
}
