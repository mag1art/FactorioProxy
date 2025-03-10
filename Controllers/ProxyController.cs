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
        /// Создаёт новый UDP-прокси для подключения к серверу Factorio.
        /// </summary>
        /// <returns>Строка в формате "адрес:порт".</returns>
        [HttpPost]
        public async Task<IActionResult> CreateProxy()
        {
            // Если у клиента уже есть cookie, блокируем повторное создание
            if (Request.Cookies.ContainsKey("ProxyContainer"))
            {
                return BadRequest(new { message = "Прокси контейнер уже создан для текущего пользователя." });
            }

            var result = await _proxyService.CreateProxy();
            if (string.IsNullOrEmpty(result))
            {
                return StatusCode(500, "Ошибка при создании прокси");
            }

            // Предполагаем, что result имеет формат "publicAddress:port"
            string[] parts = result.Split(':');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int port))
            {
                return Ok(new { address = result });
            }

            var lifetimeStr = Environment.GetEnvironmentVariable("PROXY_LIFETIME") ?? "59";
            // Устанавливаем cookie с временем жизни, например, 59 минут (или значение из PROXY_LIFETIME)
            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddMinutes(int.TryParse(lifetimeStr, out var lifetime) ? lifetime : 59)
            };
            Response.Cookies.Append("ProxyContainer", port.ToString(), cookieOptions);

            return Ok(new { address = result });
        }

        /// <summary>
        /// Останавливает запущенный прокси по указанному порту.
        /// </summary>
        /// <param name="port">Порт, на котором работает прокси.</param>
        [HttpDelete]
        public async Task<IActionResult> RemoveProxy()
        {
            if (!Request.Cookies.ContainsKey("ProxyContainer"))
            {
                return BadRequest(new { message = "Прокси контейнер не найден." });
            }

            string portCookie = Request.Cookies["ProxyContainer"];
            if (!int.TryParse(portCookie, out int port))
            {
                return BadRequest(new { message = "Неверное значение порта в куке." });
            }

            bool removed = await _proxyService.RemoveProxy(port);
            if (removed)
            {
                // Удаляем cookie после успешного удаления контейнера
                Response.Cookies.Delete("ProxyContainer");
                return Ok(new { message = "Прокси остановлен" });
            }
            else
            {
                return NotFound(new { message = "Прокси не найден или ошибка при остановке" });
            }
        }
    }
}
