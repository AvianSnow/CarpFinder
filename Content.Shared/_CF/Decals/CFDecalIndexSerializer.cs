using System.Globalization;
using Content.Shared.Decals;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared.CF.Decals;

/// <summary>
/// Reads and writes a <see cref="DecalIndex"/> as the string its <c>ToString</c> gives, <c>x,y:id</c>.
/// Upstream has no serializer for it, so a map whose <c>Biome</c> has loaded decals could not be saved:
/// <c>BiomeComponent.LoadedDecals</c> keys a dictionary by it.
/// </summary>
[TypeSerializer]
public sealed class CFDecalIndexSerializer : ITypeSerializer<DecalIndex, ValueDataNode>, ITypeCopyCreator<DecalIndex>
{
    public DecalIndex Read(ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<DecalIndex>? instanceProvider = null)
    {
        if (!TryParse(node.Value, out var index))
            throw new InvalidMappingException($"Could not parse {nameof(DecalIndex)}: '{node.Value}'");

        return index;
    }

    public ValidationNode Validate(ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        return TryParse(node.Value, out _)
            ? new ValidatedValueNode(node)
            : new ErrorNode(node, $"Failed parsing values for {nameof(DecalIndex)}.");
    }

    public DataNode Write(ISerializationManager serializationManager,
        DecalIndex value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        return new ValueDataNode(value.ToString());
    }

    public DecalIndex CreateCopy(ISerializationManager serializationManager,
        DecalIndex source,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        return new DecalIndex(source.Chunk, source.Id);
    }

    private static bool TryParse(string text, out DecalIndex index)
    {
        index = default;
        var colon = text.LastIndexOf(':');
        if (colon < 0)
            return false;

        var parts = text.Substring(0, colon).Split(',');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !ushort.TryParse(text.AsSpan(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return false;

        index = new DecalIndex(new Vector2i(x, y), id);
        return true;
    }
}
