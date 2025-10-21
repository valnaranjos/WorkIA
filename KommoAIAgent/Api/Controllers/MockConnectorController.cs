using Microsoft.AspNetCore.Mvc;

namespace KommoAIAgent.Api.Controllers;

/// <summary>
/// 🧪 Mock temporal para probar conectores SIN microservicio real
/// DELETE este archivo cuando tengas el microservicio de Calculaser funcionando
/// </summary>
[ApiController]
[Route("mock/isalud")]
public class MockConnectorController : ControllerBase
{
    private readonly ILogger<MockConnectorController> _logger;

    public MockConnectorController(ILogger<MockConnectorController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Simula el endpoint del microservicio de Calculaser
    /// POST /mock/isalud
    /// </summary>
    /// <summary>
    /// Simula el endpoint del microservicio de Calculaser
    /// POST /mock/isalud
    /// </summary>
    [HttpPost]
    public IActionResult HandleMockAction([FromBody] MockConnectorRequest request)
    {
        _logger.LogInformation(
            "🧪 MOCK: Recibido capability={Capability}, params={Params}",
            request.Capability,
            System.Text.Json.JsonSerializer.Serialize(request.Parameters)
        );

        // 🔧 FIX: Retornar object (formato camelCase - más natural en JSON)
        object response = request.Capability switch
        {
            "cancel_appointment" => new
            {
                success = true,
                message = "✅ [MOCK] Tu cita fue cancelada exitosamente",
                data = new
                {
                    agendaId = 999999,
                    fecha = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
                    hora = "10:00 AM"
                }
            },

            "get_patient_appointments" => new
            {
                success = true,
                message = "[MOCK] Citas encontradas",
                data = new[]
                {
                new { fecha = "2025-01-25", hora = "10:00 AM", profesional = "Dr. Pérez" },
                new { fecha = "2025-02-10", hora = "14:30 PM", profesional = "Dra. López" }
            }
            },

            "reschedule_appointment" => new
            {
                success = true,
                message = "✅ [MOCK] Tu cita fue reagendada",
                data = new { nuevaFecha = "2025-02-15", hora = "11:00 AM" }
            },

            "get_patient_info" => new
            {
                success = true,
                message = "[MOCK] Información del paciente",
                data = new
                {
                    nombre = "Juan Pérez",
                    documento = "12345678",
                    telefono = "3001234567",
                    email = "juan@example.com"
                }
            },

            _ => new
            {
                success = false,
                message = $"[MOCK] Capability '{request.Capability}' no soportada",
                errorDetails = "Capability desconocida",
                errorCode = "UNSUPPORTED_CAPABILITY"
            }
        };

        return Ok(response);
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "healthy", service = "mock-isalud" });
}

public record MockConnectorRequest(
    string Capability,
    Dictionary<string, object> Parameters
);