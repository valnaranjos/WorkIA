namespace KommoAIAgent.Application.Interfaces
{
    /// <summary>
    /// Interfaz de un servicio de incrustación de texto para convertir texto en vectores numéricos.
    /// </summary>
    public interface IEmbedder
    {
        //Tarea asincrónica para incrustar un solo texto y devolver su vector de incrustación.
        Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default);

        //Tarea asincrónica para incrustar un lote de textos y devolver una matriz de vectores de incrustación.
        Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);

        //Dimensiones del vector de incrustación devuelto por el servicio.
        int Dimensions { get; }

        //Nombre o identificador del modelo de incrustación utilizado.
        string Model { get; }
    }
}
