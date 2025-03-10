using FactorioProxy.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FactorioProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly ProxyService _proxyService;

        public ProxyController(ProxyService proxyService)
        {
            _proxyService = proxyService;
        }

        /// <summary>
        /// Creates a new UDP proxy container for connecting to the Factorio server.
        /// Returns a string in the format "address:port" and sets a cookie with the port.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateProxy()
        {
            // Check if the client already has a proxy cookie.
            if (Request.Cookies.ContainsKey("ProxyContainer"))
            {
                var publicAddress = Environment.GetEnvironmentVariable("PUBLIC_ADDRESS") ?? string.Empty;
                string portCookie = Request.Cookies["ProxyContainer"];
                return BadRequest(new { message = $"A proxy container has already been created for this user. {publicAddress}:{portCookie}" });
            }

            var result = await _proxyService.CreateProxy();
            if (string.IsNullOrEmpty(result))
            {
                return StatusCode(500, "Error creating proxy container.");
            }

            // Assume result is in the format "publicAddress:port"
            string[] parts = result.Split(':');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int port))
            {
                return Ok(new { address = result });
            }

            var lifetimeStr = Environment.GetEnvironmentVariable("PROXY_LIFETIME") ?? "59";
            // Set a cookie with the port value and an expiration time equal to the proxy lifetime (e.g., 59 minutes)
            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddMinutes(int.TryParse(lifetimeStr, out var lifetime) ? lifetime : 59)
            };
            Response.Cookies.Append("ProxyContainer", port.ToString(), cookieOptions);

            return Ok(new { address = result });
        }

        /// <summary>
        /// Stops the running proxy container based on the port stored in the cookie.
        /// </summary>
        [HttpDelete]
        public async Task<IActionResult> RemoveProxy()
        {
            // Retrieve the proxy container port from the cookie.
            if (!Request.Cookies.ContainsKey("ProxyContainer"))
            {
                return BadRequest(new { message = "Proxy container not found." });
            }

            string portCookie = Request.Cookies["ProxyContainer"];
            if (!int.TryParse(portCookie, out int port))
            {
                return BadRequest(new { message = "Invalid port value in cookie." });
            }

            bool removed = await _proxyService.RemoveProxy(port);
            if (removed)
            {
                // Remove the cookie after successful removal of the container.
                Response.Cookies.Delete("ProxyContainer");
                return Ok(new { message = "Proxy container has been stopped." });
            }
            else
            {
                return NotFound(new { message = "Proxy container not found or error during removal." });
            }
        }
    }
}