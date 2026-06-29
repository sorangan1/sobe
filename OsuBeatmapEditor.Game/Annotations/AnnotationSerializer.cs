using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuBeatmapEditor.Game.Annotations
{
    /// <summary>JSON (de)serialization for the <c>.sobemod</c> Review layer. Tolerant of missing/extra fields.</summary>
    public static class AnnotationSerializer
    {
        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string Serialize(AnnotationDocument doc) => JsonSerializer.Serialize(doc, options);

        /// <summary>Parses a document, or null if the text isn't a valid sobemod layer.</summary>
        public static AnnotationDocument? Deserialize(string json)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<AnnotationDocument>(json, options);
                if (doc == null || doc.Format != AnnotationDocument.FileFormat)
                    return null;
                doc.Annotations ??= new System.Collections.Generic.List<Annotation>();
                return doc;
            }
            catch
            {
                return null;
            }
        }
    }
}
