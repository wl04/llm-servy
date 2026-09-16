using System.Text.Json;
using LlmServy.Services;

namespace LlmServy.Runtime;

public sealed class LlamaClient(HttpClient http)
{
    public async Task<bool> IsReadyAsync(string root, CancellationToken token)
    {
        try
        {
            using var response = await http.GetAsync(root + "/health", token);
            if (!response.IsSuccessStatusCode)
                return false;
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (body.RootElement.ValueKind != JsonValueKind.Object ||
                !body.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
                throw new AppException(new("InvalidResponse"));
            return status.GetString() == "ok";
        }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return false; }
        catch (HttpRequestException) { return false; }
        catch (JsonException error) { throw new AppException(new("InvalidResponse"), error); }
    }

    public async Task LoadModelAsync(string root, string modelId, CancellationToken token)
    {
        try
        {
            using var modelsResponse = await http.GetAsync(root + "/v1/models", token);
            if (!modelsResponse.IsSuccessStatusCode)
                throw new AppException(new("HttpFailure", (int)modelsResponse.StatusCode));
            using var models = JsonDocument.Parse(await modelsResponse.Content.ReadAsStringAsync(token));
            if (models.RootElement.ValueKind != JsonValueKind.Object || !models.RootElement.TryGetProperty("data", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new AppException(new("InvalidResponse"));
            bool found = false;
            foreach (var model in list.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object || !model.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
                    throw new AppException(new("InvalidResponse"));
                if (id.GetString() == modelId)
                    found = true;
            }
            if (!found)
                throw new AppException(new("MissingModel", modelId));
            // This HTTP GET has server-side effects. Expose it as a command at our boundary.
            using var response = await http.GetAsync(root + "/props?autoload=true&model=" + Uri.EscapeDataString(modelId), token);
            if (!response.IsSuccessStatusCode)
                throw new AppException(new("ModelLoadFailed", (int)response.StatusCode));
            using var props = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (props.RootElement.ValueKind != JsonValueKind.Object)
                throw new AppException(new("InvalidResponse"));
            if (!props.RootElement.TryGetProperty("default_generation_settings", out _))
                throw new AppException(new("ModelNotReady"));
        }
        catch (JsonException error) { throw new AppException(new("InvalidResponse"), error); }
    }
}
