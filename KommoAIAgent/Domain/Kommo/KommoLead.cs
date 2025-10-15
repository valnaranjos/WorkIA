namespace KommoAIAgent.Domain.Kommo
{
    //Clase que representa un lead en Kommo, con campos básicos y personalizados.
    public class KommoLead
    {
        public long Id { get; set; }

        public string Name { get; set; } = "Cliente"; // Valor por defecto

        public List<string> Tags { get; set; } = [];

        // En el futuro, podríamos añadir aquí cualquier otro campo personalizado
        // que sea relevante para dar contexto a la IA.
    }
}
