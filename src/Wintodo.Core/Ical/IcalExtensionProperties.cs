using System.Text;

namespace Wintodo.Core.Ical;

/// <summary>A non-standard <c>VTODO</c> property kept verbatim in <c>ical_unknown</c>.</summary>
public sealed record IcalExtensionProperty(
    string Name,
    string Value,
    IReadOnlyList<KeyValuePair<string, string>>? Parameters = null);

/// <summary>
/// Adds and reads extension properties (for example <c>X-MSTODO-*</c>) in the
/// unknown-property passthrough, so importers can keep source values that
/// Wintodo does not model. Values are written as escaped, folded content lines.
/// </summary>
public static class IcalExtensionProperties
{
    /// <summary>
    /// Returns <paramref name="existingUnknown"/> with every <c>VTODO</c> property
    /// named in <paramref name="replacedNames"/> removed and
    /// <paramref name="properties"/> appended. All other entries are kept as-is.
    /// </summary>
    public static string? Merge(
        string? existingUnknown,
        IEnumerable<IcalExtensionProperty> properties,
        IReadOnlyCollection<string> replacedNames)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(replacedNames);
        var entries = IcalUnknownCodec.Decode(existingUnknown)
            .Where(entry => entry.Context != IcalUnknownCodec.VTodoContext
                || !replacedNames.Contains(ReadName(entry.Raw), StringComparer.OrdinalIgnoreCase))
            .ToList();
        foreach (IcalExtensionProperty property in properties)
        {
            entries.Add(new IcalUnknownEntry(IcalUnknownCodec.VTodoContext, Format(property)));
        }

        return IcalUnknownCodec.Encode(entries);
    }

    /// <summary>
    /// Replaces the <paramref name="replacedNames"/> properties of
    /// <paramref name="existingUnknown"/> with every <c>VTODO</c> property of
    /// <paramref name="incomingUnknown"/>. Entries from other clients survive.
    /// </summary>
    public static string? Overlay(
        string? existingUnknown,
        string? incomingUnknown,
        IReadOnlyCollection<string> replacedNames)
    {
        ArgumentNullException.ThrowIfNull(replacedNames);
        var entries = IcalUnknownCodec.Decode(existingUnknown)
            .Where(entry => entry.Context != IcalUnknownCodec.VTodoContext
                || !replacedNames.Contains(ReadName(entry.Raw), StringComparer.OrdinalIgnoreCase))
            .ToList();
        entries.AddRange(IcalUnknownCodec.Decode(incomingUnknown)
            .Where(entry => entry.Context == IcalUnknownCodec.VTodoContext));
        return IcalUnknownCodec.Encode(entries);
    }

    /// <summary>Unescaped values of every <c>VTODO</c> property called <paramref name="name"/>.</summary>
    public static IReadOnlyList<string> ReadValues(string? unknown, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var values = new List<string>();
        foreach (IcalUnknownEntry entry in IcalUnknownCodec.Decode(unknown))
        {
            if (entry.Context != IcalUnknownCodec.VTodoContext)
            {
                continue;
            }

            string line = Unfold(entry.Raw);
            if (!string.Equals(ReadName(line), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int colon = FindValueSeparator(line);
            values.Add(colon < 0 ? string.Empty : UnescapeText(line[(colon + 1)..]));
        }

        return values;
    }

    private static string Format(IcalExtensionProperty property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property.Name);
        if (property.Name.IndexOfAny([':', ';', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("Property names cannot contain separators.", nameof(property));
        }

        var line = new StringBuilder(property.Name);
        foreach (KeyValuePair<string, string> parameter in property.Parameters ?? [])
        {
            line.Append(';').Append(parameter.Key).Append('=')
                .Append(IcalWriter.EscapeParameter(parameter.Value.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        line.Append(':').Append(IcalWriter.EscapeText(property.Value));
        var output = new StringBuilder();
        IcalWriter.AppendLine(output, line.ToString());
        return output.ToString();
    }

    private static string ReadName(string raw)
    {
        int end = raw.IndexOfAny([';', ':']);
        return (end < 0 ? raw : raw[..end]).Trim();
    }

    private static int FindValueSeparator(string line)
    {
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (character == ':' && !quoted)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unfold(string raw)
    {
        return raw
            .Replace("\r\n ", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n\t", string.Empty, StringComparison.Ordinal)
            .Replace("\n ", string.Empty, StringComparison.Ordinal)
            .TrimEnd('\r', '\n');
    }

    private static string UnescapeText(string value)
    {
        var output = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character != '\\' || index + 1 >= value.Length)
            {
                output.Append(character);
                continue;
            }

            char next = value[++index];
            output.Append(next is 'n' or 'N' ? '\n' : next);
        }

        return output.ToString();
    }
}
