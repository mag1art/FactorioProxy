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
            var result = await _proxyService.CreateProxy();
            if (string.IsNullOrEmpty(result))
            {
                return StatusCode(500, "Ошибка при создании прокси");
            }
            return Ok(new { address = result });
        }

        /// <summary>
        /// Останавливает запущенный прокси по указанному порту.
        /// </summary>
        /// <param name="port">Порт, на котором работает прокси.</param>
        [HttpDelete("{port}")]
        public async Task<IActionResult> RemoveProxy(int port)
        {
            bool removed = await _proxyService.RemoveProxy(port);
            if (removed)
                return Ok(new { message = "Прокси остановлен" });
            else
                return NotFound(new { message = "Прокси не найден или ошибка при остановке" });
        }
    }
}
