namespace Looxdex.Api.Services;

/// <summary>
/// Identifies whose closet and saved looks a request refers to.
///
/// Today this is an anonymous device id the client generates and stores locally,
/// which is enough to stop every visitor sharing one global state. When real auth
/// lands, this reads the subject claim instead and nothing else has to change.
/// </summary>
public interface IOwnerContext
{
    string OwnerKey { get; }
}

public class HeaderOwnerContext : IOwnerContext
{
    public const string HeaderName = "X-Looxdex-Owner";

    /// <summary>Used when a client sends no id, so the seeded demo content still resolves.</summary>
    public const string DemoOwnerKey = "demo";

    private readonly IHttpContextAccessor _accessor;

    public HeaderOwnerContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string OwnerKey
    {
        get
        {
            var raw = _accessor.HttpContext?.Request.Headers[HeaderName].ToString();
            if (string.IsNullOrWhiteSpace(raw)) return DemoOwnerKey;

            var trimmed = raw.Trim();
            if (trimmed.Length > 64) trimmed = trimmed[..64];

            // Keep it to characters that are safe as a database key.
            return trimmed.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
                ? trimmed
                : DemoOwnerKey;
        }
    }
}
