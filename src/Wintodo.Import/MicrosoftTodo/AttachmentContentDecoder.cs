using System.Runtime.Serialization.Json;
using System.Xml;

namespace Wintodo.Import.MicrosoftTodo;

internal static class AttachmentContentDecoder
{
    internal static async Task<long> CopyAsync(Stream source, Stream destination,
        Action<long>? progress, CancellationToken cancellationToken, long maximumBytes)
    {
        // The BCL JSON reader exposes strings through a streaming XML infoset.
        // No serializer, reflection, or full contentBytes string is required.
        var quotas = new XmlDictionaryReaderQuotas
        {
            MaxDepth = 64,
            MaxStringContentLength = 40 * 1024 * 1024,
            MaxArrayLength = 64 * 1024,
            MaxBytesPerRead = 64 * 1024,
            MaxNameTableCharCount = 16 * 1024,
        };
        using XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(source, quotas);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        bool advance = true;
        bool foundContent = false;
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (advance && !reader.Read()) break;
            advance = true;
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1 || reader.LocalName != "contentBytes")
            {
                continue;
            }

            if (foundContent)
                throw new InvalidDataException("The attachment response contains duplicate content fields.");
            foundContent = true;

            if (reader.GetAttribute("type") == "null")
            {
                continue;
            }

            int read;
            while ((read = reader.ReadElementContentAsBase64(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (read > maximumBytes - total)
                    throw new InvalidDataException("The attachment exceeds the byte limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                progress?.Invoke(total);
            }
            // ReadElementContentAsBase64 already advances to the next node;
            // do not skip a sibling or trailing malformed input.
            advance = false;
        }
        return total;
    }
}
