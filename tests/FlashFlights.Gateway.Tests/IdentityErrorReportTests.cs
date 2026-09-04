using FlashFlights.Gateway.Identity;
using Microsoft.AspNetCore.Identity;

namespace FlashFlights.Gateway.Tests;

/// <summary>
/// Identity's refusals arrive as codes and prose; an auth form needs them
/// grouped by the field the buyer can actually fix. This is the mapping in
/// between, tested without a request because that is all it is.
/// </summary>
public class IdentityErrorReportTests
{
    [Theory]
    [InlineData("PasswordTooShort", IdentityErrorReport.PasswordField)]
    [InlineData("PasswordRequiresDigit", IdentityErrorReport.PasswordField)]
    [InlineData("InvalidEmail", IdentityErrorReport.EmailField)]
    [InlineData("DuplicateEmail", IdentityErrorReport.EmailField)]
    // Email is the username here, so a complaint about one belongs to the field
    // the form actually shows.
    [InlineData("InvalidUserName", IdentityErrorReport.EmailField)]
    [InlineData("DuplicateUserName", IdentityErrorReport.EmailField)]
    public void Each_refusal_is_reported_against_the_field_that_can_fix_it(string code, string field)
    {
        var errors = IdentityErrorReport.ToValidationErrors([Error(code)]);

        Assert.Equal([field], errors.Keys);
    }

    /// <summary>
    /// An unrecognised code still has to reach the buyer. The empty key is
    /// ModelState's own convention for an error that belongs to the request
    /// rather than one field.
    /// </summary>
    [Fact]
    public void A_refusal_that_belongs_to_no_field_is_still_reported()
    {
        var errors = IdentityErrorReport.ToValidationErrors([Error("ConcurrencyFailure", "Optimistic concurrency failure.")]);

        Assert.Equal(["Optimistic concurrency failure."], errors[IdentityErrorReport.GeneralField]);
    }

    /// <summary>
    /// Registering an already-registered email trips two codes for one
    /// underlying problem, and Identity's own wording for them names a
    /// "username" the form never showed.
    /// </summary>
    [Fact]
    public void A_taken_email_is_reported_once_and_in_the_form_s_own_terms()
    {
        var errors = IdentityErrorReport.ToValidationErrors([
            Error("DuplicateUserName", "Username 'buyer@flashflights.test' is already taken."),
            Error("DuplicateEmail", "Email 'buyer@flashflights.test' is already taken."),
        ]);

        Assert.Equal([IdentityErrorReport.EmailAlreadyRegistered], errors[IdentityErrorReport.EmailField]);
    }

    [Fact]
    public void Refusals_on_different_fields_stay_apart()
    {
        var errors = IdentityErrorReport.ToValidationErrors([
            Error("InvalidEmail", "Email 'nope' is invalid."),
            Error("PasswordTooShort", "Passwords must be at least 8 characters."),
        ]);

        Assert.Equal(["Email 'nope' is invalid."], errors[IdentityErrorReport.EmailField]);
        Assert.Equal(["Passwords must be at least 8 characters."], errors[IdentityErrorReport.PasswordField]);
    }

    private static IdentityError Error(string code, string description = "Refused.") =>
        new() { Code = code, Description = description };
}
