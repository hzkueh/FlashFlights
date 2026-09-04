using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FlashFlights.ServiceDefaults.Authentication;

/// <summary>
/// The signing config the whole system shares. The gateway signs with it and
/// Catalog, Ordering, and Notifications validate against it — one symmetric key
/// rather than a key per service, which is what lets auth be a lightweight
/// piece alongside the gateway instead of a fourth microservice with its own
/// key distribution problem.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section every service binds this from.</summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// The minimum key length HMAC-SHA256 will accept: 256 bits. Configuring
    /// anything shorter throws deep inside the signing call, so it is checked
    /// here instead, at startup, where the message can name the setting.
    /// </summary>
    public const int MinimumSigningKeyLength = 32;

    [Required]
    public string Issuer { get; set; } = "flashflights";

    [Required]
    public string Audience { get; set; } = "flashflights";

    /// <summary>
    /// Shared secret, supplied per environment. Deliberately has no default: a
    /// fallback here would be a signing key committed to a public repo, and
    /// every service would silently accept tokens signed with it.
    /// </summary>
    [Required]
    [MinLength(MinimumSigningKeyLength)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// How long an issued token stays valid. Long enough that a buyer is not
    /// signed out mid-checkout, short enough that a leaked token expires.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>The key both signing and validation are derived from.</summary>
    public SymmetricSecurityKey CreateSecurityKey() =>
        new(Encoding.UTF8.GetBytes(SigningKey));
}
