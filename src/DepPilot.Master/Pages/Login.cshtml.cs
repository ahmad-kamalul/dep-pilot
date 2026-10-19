using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DepPilot.Master.Pages;

[EnableRateLimiting("login")]
public sealed class LoginModel(AdminCredential credential) : PageModel
{
    [BindProperty, Required, StringLength(120)] public string Username { get; set; } = "";
    [BindProperty, Required, StringLength(512)] public string Password { get; set; } = "";
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? RedirectToPage("/Index") : Page();
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var validPassword = new PasswordHasher<string>().VerifyHashedPassword(credential.Username, credential.PasswordHash, Password) != PasswordVerificationResult.Failed;
        if (!validPassword || !string.Equals(Username, credential.Username, StringComparison.Ordinal))
        {
            ModelState.AddModelError("", "The username or password is incorrect.");
            return Page();
        }
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, credential.Username)], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return RedirectToPage("/Index");
    }
}
