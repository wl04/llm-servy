namespace LlmServy.Services;

/// <summary>Attempts every release operation and preserves all cleanup failures.</summary>
internal static class ResourceCleanup
{
    public static void Run(params Action[] operations)
    {
        var failures = new List<Exception>();
        foreach (var operation in operations)
        {
            try
            {
                operation();
            }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count > 0)
            throw new AggregateException(failures);
    }
}
