using System.Globalization;
using System.Resources;
using LlmServy.Services;

namespace LlmServy.Localization;

public sealed class Localizer
{
    private static readonly ResourceManager Resources = new("LlmServy.Localization.Strings", typeof(Localizer).Assembly);
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public event Action? Changed;

    public static CultureInfo GetCulture(string language, CultureInfo systemCulture) =>
        CultureInfo.GetCultureInfo((language == "system" ? systemCulture.TwoLetterISOLanguageName : language) == "ru" ? "ru" : "en");

    public void SetLanguage(string language)
    {
        Culture = GetCulture(language, CultureInfo.InstalledUICulture);
        Changed?.Invoke();
    }

    public string Get(string key, params object[] args)
    {
        var template = Resources.GetString(key, Culture) ?? throw new MissingManifestResourceException(key);
        return args.Length == 0 ? template : string.Format(Culture, template, args);
    }

    public string Format(AppMessage message) => Get(message.Code, message.Arguments.Select(a => a is AppMessage nested ? Format(nested) : a).ToArray());

    public string Error(Exception error) => error switch
    {
        AppException known => Format(known.Detail) + (known.InnerException is null ? "" : Environment.NewLine + Error(known.InnerException)),
        AggregateException group => string.Join(Environment.NewLine, group.InnerExceptions.Select(Error)),
        OperationCanceledException => Get("Cancelled"),
        _ => Get("UnexpectedError", error.GetType().Name) + (error.InnerException is null ? "" : Environment.NewLine + Error(error.InnerException))
    };
}
