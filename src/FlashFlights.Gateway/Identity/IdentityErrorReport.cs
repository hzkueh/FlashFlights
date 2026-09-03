using Microsoft.AspNetCore.Identity;

namespace FlashFlights.Gateway.Identity;

/// <summary>
/// Turns what Identity refused into something an auth form can render next to
/// the field that caused it. Kept apart from the endpoints so the mapping — the
/// part that is easy to get subtly wrong — is testable without a request.
/// </summary>
public static class IdentityErrorReport
{
    /// <summary>Field keys the SPA's forms know about.</summary>
    public const string EmailField = "email";

    /// <inheritdoc cref="EmailField"/>
    public const string PasswordField = "password";

    /// <summary>
    /// Anything that belongs to the request rather than one field. The empty
    /// key is ModelState's own convention for a model-level error, so a client
    /// that groups by field already has somewhere to put these.
    /// </summary>
    public const string GeneralField = "";

    /// <summary>
    /// Identity reports a taken email as a taken *username*, because email is
    /// what we register as. Saying so would expose an implementation detail as
    /// user-facing copy, so both duplicate codes are answered with one message.
    /// </summary>
    public const string EmailAlreadyRegistered = "That email is already registered.";

    /// <summary>
    /// Grouped by field and de-duplicated: registering an already-registered
    /// email trips two Identity codes for the same underlying problem, and a
    /// form showing the same sentence twice reads like a bug.
    /// </summary>
    public static Dictionary<string, string[]> ToValidationErrors(IEnumerable<IdentityError> errors) =>
        errors
            .Select(error => (Field: FieldFor(error.Code), Message: MessageFor(error)))
            .GroupBy(entry => entry.Field)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.Message).Distinct().ToArray());

    private static string FieldFor(string code) => code switch
    {
        var _ when code.Contains("Password", StringComparison.Ordinal) => PasswordField,
        var _ when code.Contains("Email", StringComparison.Ordinal) => EmailField,
        // Email is the username, so a complaint about one is a complaint about
        // the field the form actually shows.
        var _ when code.Contains("UserName", StringComparison.Ordinal) => EmailField,
        _ => GeneralField,
    };

    private static string MessageFor(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" or "DuplicateEmail" => EmailAlreadyRegistered,
        _ => error.Description,
    };
}
