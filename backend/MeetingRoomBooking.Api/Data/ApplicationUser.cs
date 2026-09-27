using Microsoft.AspNetCore.Identity;

namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// The application's user type. Kept as its own (currently empty) subclass
/// of <see cref="IdentityUser"/>, rather than using <see cref="IdentityUser"/>
/// directly, so profile fields can be added later without an identity
/// -breaking migration of the user table's CLR type.
/// </summary>
public class ApplicationUser : IdentityUser
{
}
