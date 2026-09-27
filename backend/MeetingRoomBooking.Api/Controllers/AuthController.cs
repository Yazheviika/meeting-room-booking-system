using MeetingRoomBooking.Api.Data;
using MeetingRoomBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRoomBooking.Api.Controllers;

/// <summary>Registration, login, and current-user endpoints.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;

    /// <summary>Initializes a new instance of the <see cref="AuthController"/> class.</summary>
    public AuthController(UserManager<ApplicationUser> userManager, IJwtTokenService jwtTokenService)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>
    /// Creates a new account. Always assigns the "User" role — there is no
    /// way for a caller to register as "Admin"; the only admin account is
    /// the one optionally seeded at startup from configuration.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code, error.Description);
            }

            return ValidationProblem(ModelState);
        }

        await _userManager.AddToRoleAsync(user, "User");

        return await BuildAuthResponseAsync(user);
    }

    /// <summary>
    /// Validates credentials and returns a bearer token. Returns a bare 401
    /// whether the account doesn't exist or the password is wrong — never
    /// reveals which, to avoid leaking which emails are registered.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized();
        }

        return await BuildAuthResponseAsync(user);
    }

    /// <summary>
    /// Returns the current authenticated user's info and roles. Unlike
    /// register/login, this does not mint a new token — it just reports
    /// who the caller's existing token belongs to.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserInfoResponse>> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new UserInfoResponse(user.Id, user.Email!, roles.ToList()));
    }

    private async Task<ActionResult<AuthResponse>> BuildAuthResponseAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtTokenService.CreateToken(user, roles);
        return Ok(new AuthResponse(token.AccessToken, token.ExpiresAtUtc, user.Id, user.Email!, roles.ToList()));
    }
}

/// <summary>Request body for <see cref="AuthController.Register"/>.</summary>
public record RegisterRequest(string Email, string Password);

/// <summary>Request body for <see cref="AuthController.Login"/>.</summary>
public record LoginRequest(string Email, string Password);

/// <summary>Response returned by register and login — includes a fresh access token.</summary>
public record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, string UserId, string Email, IReadOnlyCollection<string> Roles);

/// <summary>Response returned by the current-user endpoint — no token.</summary>
public record UserInfoResponse(string UserId, string Email, IReadOnlyCollection<string> Roles);
