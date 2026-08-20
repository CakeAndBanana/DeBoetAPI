using System.Security.Claims;

namespace DeBoetAPI.Common;

public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public Guid? Id
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

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
