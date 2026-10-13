namespace YTubeFetch.Services;

/// <summary>
/// How a yt-dlp failure should be treated on retry.
/// </summary>
public enum YtDlpErrorKind
{
    /// <summary>Not known to be transient — fail the job immediately.</summary>
    Permanent,

    /// <summary>HTTP 429. YouTube is throttling us; back off for a long while.</summary>
    RateLimited,

    /// <summary>
    /// HTTP 403 on the media URL, or a dropped connection. YouTube's CDN rejects
    /// roughly one media request in eight for no reason tied to the video, and a
    /// fresh attempt re-extracts the URL and normally succeeds, so retry quickly.
    /// </summary>
    Transient
}

public class YtDlpException : Exception
{
    public YtDlpErrorKind Kind { get; }

    /// <summary>True when YouTube returned HTTP 429.</summary>
    public bool Is429 => Kind == YtDlpErrorKind.RateLimited;

    /// <summary>True when a fresh attempt is worth making.</summary>
    public bool IsRetryable => Kind != YtDlpErrorKind.Permanent;

    public YtDlpException(string message, YtDlpErrorKind kind) : base(message)
    {
        Kind = kind;
    }

    /// <summary>
    /// Classifies a yt-dlp stderr dump. Matching is deliberately narrow: an error
    /// we cannot place is Permanent, so a genuinely broken job fails fast instead
    /// of being retried three times.
    /// </summary>
    public static YtDlpErrorKind Classify(string errorText)
    {
        if (Contains(errorText, "HTTP Error 429") || Contains(errorText, "Too Many Requests"))
            return YtDlpErrorKind.RateLimited;

        // A 403 on a private/removed video comes with its own explanation, so only
        // treat a bare 403 on the media stream as transient.
        if (Contains(errorText, "HTTP Error 403") || Contains(errorText, "Forbidden"))
        {
            if (Contains(errorText, "Private video") || Contains(errorText, "members-only")
                || Contains(errorText, "This video is available to this channel's members")
                || Contains(errorText, "Sign in"))
                return YtDlpErrorKind.Permanent;

            return YtDlpErrorKind.Transient;
        }

        if (Contains(errorText, "HTTP Error 5")
            || Contains(errorText, "Connection reset")
            || Contains(errorText, "Connection aborted")
            || Contains(errorText, "Remote end closed connection")
            || Contains(errorText, "timed out")
            || Contains(errorText, "Read timed out")
            || Contains(errorText, "Unable to connect to proxy")
            || Contains(errorText, "temporary failure in name resolution"))
            return YtDlpErrorKind.Transient;

        return YtDlpErrorKind.Permanent;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
