namespace KeeStashDock.Core.Services;

public enum TranslationFailure
{
    None,
    InvalidInput,
    TextTooLong,
    MissingCredentials,
    UnsupportedProvider,
    Authentication,
    RateLimited,
    QuotaExceeded,
    UnsupportedLanguage,
    Timeout,
    Cancelled,
    Network,
    InvalidResponse,
    ProviderUnavailable,
    InvalidConfiguration,
    LocalServiceUnavailable,
    LanguageModelUnavailable,
    ServiceNotActivated,
}

/// <summary>Contains no source text, credentials, or raw provider error body.</summary>
public sealed record TranslationResult(
    string Provider,
    string? Text,
    TranslationFailure Failure = TranslationFailure.None,
    int? HttpStatusCode = null)
{
    public bool IsSuccess => Failure == TranslationFailure.None && !string.IsNullOrWhiteSpace(Text);
}
