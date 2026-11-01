// Adapted from KasNetWallet Core (SecretText.cs), same author, MIT License. Part of KaspaMy (MIT).
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaspaMy.Core;

/// <summary>
/// Wrapper for secret strings (mnemonic, passwords). ToString/interpolation yield "[REDACTED]" and
/// System.Text.Json refuses to serialize it. Use <see cref="Reveal"/> only at the point of use.
/// </summary>
[JsonConverter(typeof(RefuseSecretJsonConverter<SecretText>))]
public sealed class SecretText
{
    private readonly string _value;
    public SecretText(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
    public string Reveal() => _value;
    public int Length => _value.Length;
    public override string ToString() => SecretRedactor.Mask;
}

/// <summary>Throws on any attempt to (de)serialize a secret-bearing type.</summary>
public sealed class RefuseSecretJsonConverter<T> : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new InvalidOperationException($"Refusing to deserialize secret type {typeof(T).Name}.");
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        throw new InvalidOperationException($"Refusing to serialize secret type {typeof(T).Name}.");
}
