using System.Security.Claims;

namespace DeBoetAPI.Common;

/// <summary>
/// The identity behind the current request, for stamping audit records. Resolved from the
/// HTTP context rather than threaded through every service method, which would add two
/// parameters to signatures that are already long.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    /// <summary>Null outside a request — startup seeding, migrations, tests calling the service directly.</summary>
    public Guid? Id
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            // Reuses the same claim resolution the rest of the API relies on.
            try
            {
                return principal.GetUserId();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>Display name, falling back to the email and then to a marker for system actions.</summary>
    public string Name
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return "system";
            }

            return principal.FindFirstValue("display_name")
                   ?? principal.FindFirstValue("email")
                   ?? "unknown";
        }
    }
}
