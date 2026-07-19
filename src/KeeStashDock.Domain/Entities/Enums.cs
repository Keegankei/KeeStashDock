namespace KeeStashDock.Domain.Entities;

/// <summary>
/// Content classification of a snippet. Drives the card badge in the UI,
/// the editor mode (for example, a monospace font for Code) and filtering.
/// Values are persisted as integers - NEVER reorder existing members,
/// only append new ones at the end (database compatibility contract).
/// </summary>
public enum SnippetType
{
    PlainText = 0,
    Markdown  = 1,
    Code      = 2,
    Link      = 3,
    FilePath  = 4,
}

/// <summary>
/// Where a snippet came from. Used for filtering and audit-friendly metadata.
/// </summary>
public enum SnippetSource
{
    Manual    = 0,   // typed by the user in quick input or workbench editor
    Clipboard = 1,   // captured by the opt-in clipboard monitor
    Plugin    = 2,   // reserved persisted value for extension-originated content
    DragDrop  = 3,   // dropped from another application / Explorer
    Import    = 4,   // brought in by a group package (.ksdgrp) or preset
}

/// <summary>
/// Retention policy chosen when creating a snippet.
/// "Temporary stash" is the product's founding scenario, so expiry is a
/// first-class concept rather than an afterthought. The concrete ExpireAt
/// timestamp is computed at creation time from this policy.
/// </summary>
public enum ExpirePolicy
{
    Never         = 0,
    OneDay        = 1,
    SevenDays     = 2,
    BurnAfterRead = 3,   // moved to trash immediately after first copy-out
}
