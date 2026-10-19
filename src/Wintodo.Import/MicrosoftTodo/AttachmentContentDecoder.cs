using System.Runtime.Serialization.Json;
using System.Xml;

namespace Wintodo.Import.MicrosoftTodo;

internal static class AttachmentContentDecoder
{
    internal static async Task<long> CopyAsync(Stream source, Stream destination,
        Action<long>? progress, CancellationToken cancellationToken)
    {
        // The BCL JSON reader exposes strings through a streaming XML infoset.
        // No serializer, reflection, or full contentBytes string is required.
        using XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(source, XmlDictionaryReaderQuotas.Max);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1 || reader.LocalName != "contentBytes")
            {
                continue;
            }

            if (reader.GetAttribute("type") == "null")
            {
                continue;
            }

            int read;
            while ((read = reader.ReadElementContentAsBase64(buffer, 0, buffer.Length)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                progress?.Invoke(total);
            }
        }
        return total;
    }
}
