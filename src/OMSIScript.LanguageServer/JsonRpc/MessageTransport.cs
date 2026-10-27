using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OMSIScript.LanguageServer.JsonRpc;

public sealed class IncomingMessage(JsonDocument document)
{
    public JsonDocument Document { get; } = document;

    public JsonElement Root => Document.RootElement;

    public string? Method => Root.TryGetProperty("method", out var method) ? method.GetString() : null;

    public JsonElement? Id => Root.TryGetProperty("id", out var id) ? id : null;

    public JsonElement? Params => Root.TryGetProperty("params", out var parameters) ? parameters : null;

    public bool IsResponse => Method is null && Id is not null;
}

public sealed class MessageReader(Stream input)
{
    private readonly BufferedStream _input = new(input, 64 * 1024);

    public async Task<IncomingMessage?> ReadAsync(CancellationToken cancellation)
    {
        var contentLength = -1;
        while (true)
        {
            var line = await ReadHeaderLineAsync(cancellation);
            if (line is null)
                return null;
            if (line.Length == 0)
                break;

            var separator = line.IndexOf(':');
            if (separator > 0 && line[..separator].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line[(separator + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }

        if (contentLength < 0)
            throw new InvalidDataException("Missing Content-Length header.");

        var buffer = new byte[contentLength];
        await _input.ReadExactlyAsync(buffer, cancellation);
        return new IncomingMessage(JsonDocument.Parse(buffer));
    }

    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellation)
    {
        var bytes = new List<byte>(64);
        var single = new byte[1];
        while (true)
        {
            var read = await _input.ReadAsync(single, cancellation);
            if (read == 0)
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);

            if (single[0] == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r')
                    bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString([.. bytes]);
            }

            bytes.Add(single[0]);
        }
    }
}

public sealed class MessageWriter(Stream output)
{
    private readonly Lock _sync = new();

    public void WriteResponse<T>(JsonElement id, T result, JsonTypeInfo<T> typeInfo)
        => Write(writer =>
        {
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, result, typeInfo);
        });

    public void WriteNullResponse(JsonElement id)
        => Write(writer =>
        {
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WriteNull("result");
        });

    public void WriteError(JsonElement? id, int code, string message)
        => Write(writer =>
        {
            writer.WritePropertyName("id");
            if (id is { } value)
                value.WriteTo(writer);
            else
                writer.WriteNullValue();
            writer.WriteStartObject("error");
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        });

    public void WriteNotification<T>(string method, T parameters, JsonTypeInfo<T> typeInfo)
        => Write(writer =>
        {
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            JsonSerializer.Serialize(writer, parameters, typeInfo);
        });

    private void Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            body(writer);
            writer.WriteEndObject();
        }

        var header = Encoding.ASCII.GetBytes($"Content-Length: {buffer.WrittenCount}\r\n\r\n");
        lock (_sync)
        {
            output.Write(header);
            output.Write(buffer.WrittenSpan);
            output.Flush();
        }
    }
}
