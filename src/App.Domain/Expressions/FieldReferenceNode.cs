namespace App.Domain.Expressions;

/// <summary>
/// A reference to a field. When resolved, <see cref="DictionaryEntryId"/> is the linked dictionary
/// entry — references are linked, not literal, which makes lineage precise and rename-safe
/// (Architecture §7b). <see cref="Text"/> always keeps the original token (e.g. <c>a.acct_id</c> or
/// <c>lifetime_value</c>) for display and for re-resolution. A null id means the reference is
/// unresolved (flagged in the UI, never blocks save).
/// </summary>
public sealed record FieldReferenceNode(string Text, Guid? DictionaryEntryId = null) : ExpressionNode
{
    public bool IsResolved => DictionaryEntryId is not null;
}
