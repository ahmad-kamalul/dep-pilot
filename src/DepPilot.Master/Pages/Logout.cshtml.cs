using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DepPilot.Master.Pages;

public sealed class LogoutModel : PageModel
{
    public async Task<IActionResult> OnPostAsync() { await HttpContext.SignOutAsync(); return RedirectToPage("/Login"); }
    public IActionResult OnGet() => RedirectToPage("/Index");
}
