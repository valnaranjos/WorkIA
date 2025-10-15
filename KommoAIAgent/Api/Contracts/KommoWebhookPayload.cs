using KommoAIAgent.Application.Common;
using Newtonsoft.Json;

namespace KommoAIAgent.Api.Contracts
{
    public class KommoWebhookPayload
    {
        // Usamos el atributo [JsonProperty] para decirle al deserializador cómo se llama
        // el campo en el JSON que viene de Kommo.
        [JsonProperty("message")]
        public MessageData? Message { get; set; }
    }

    // Esta clase representa la sección "message" dentro del payload.
    public class MessageData
    {
        // El atributo "add" de Kommo en realidad es un array,
        // por eso lo definimos como una lista.
        [JsonProperty("add")]
        public List<MessageDetails>? AddedMessages { get; set; }
    }

    // Esta clase contiene los detalles específicos de cada mensaje nuevo.
    public class MessageDetails
    {
        [JsonProperty("id")]
        public string? MessageId { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; } // Será "incoming" para los mensajes de clientes.

        [JsonProperty("text")]
        public string? Text { get; set; }

        [JsonProperty("chat_id")]
        public string? ChatId { get; set; }

        // Este campo es crucial, nos dice a qué Lead pertenece el mensaje.
        [JsonProperty("entity_id")]
        public long? LeadId { get; set; }

        [JsonProperty("entity_type")]
        public string? EntityType { get; set; } // Debería ser "leads".

        //Lista de adjuntos (AttachmentInfo) si los hay
        public List<AttachmentInfo> Attachments { get; set; } = [];
    }
}
