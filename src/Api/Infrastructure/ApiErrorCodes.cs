namespace Degerli.Api.Infrastructure;

/// <summary>
/// Stable machine error-code catalog (`03` §7, `01` §10.2). The SPA maps these codes
/// to localized copy; API responses never carry user-facing prose for errors.
/// </summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string MetricNotAvailable = "METRIC_NOT_AVAILABLE";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Forbidden = "FORBIDDEN";
    public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";
    public const string NotFound = "NOT_FOUND";
    public const string EmailTaken = "EMAIL_TAKEN";
    public const string DuplicateName = "DUPLICATE_NAME";
    public const string DcfNotComputable = "DCF_NOT_COMPUTABLE";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string LockedOut = "LOCKED_OUT";
    public const string RateLimited = "RATE_LIMITED";
    public const string Internal = "INTERNAL";
    public const string Conflict = "CONFLICT";
}
