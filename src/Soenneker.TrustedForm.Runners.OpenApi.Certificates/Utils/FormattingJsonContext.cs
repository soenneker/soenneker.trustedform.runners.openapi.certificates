using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soenneker.TrustedForm.Runners.OpenApi.Certificates.Utils;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(JsonDocument))]
internal partial class FormattingJsonContext : JsonSerializerContext;
