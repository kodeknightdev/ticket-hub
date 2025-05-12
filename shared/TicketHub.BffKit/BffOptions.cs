namespace TicketHub.BffKit;

public sealed class BffOptions
{
    public const string Section = "Bff";

    /// <summary>Short app name; drives the cookie name, data-protection purpose and Redis key prefix.</summary>
    public string AppName { get; set; } = "app";
    public string CookieName { get; set; } = "__Host-tickethub";
    public string FrontendOrigin { get; set; } = "http://localhost:3000";
    public string[] AllowedRoles { get; set; } = [];
    public string TokenStore { get; set; } = "redis";
    public int SessionHours { get; set; } = 8;
    public string IdentityUrl { get; set; } = "http://localhost:5201";
}
