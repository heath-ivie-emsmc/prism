using System.Collections.Frozen;
using System.Text;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// What the policy found in one SQL text: the first rule it breaks (or null), and whether the text uses the <c>@provider_id</c> parameter
/// in code (not inside a literal or a comment).
/// </summary>
internal sealed record WarehouseSqlAnalysis(string? Violation, bool UsesProviderParameter);

/// <summary>
/// The warehouse is read-only, and this is the second of its layers (the first is the database login, the third a read-only transaction).
/// It reads the SQL text the way PostgreSQL does, one linear pass with no regular expressions: string literals (plain, <c>E''</c>, dollar-quoted),
/// quoted identifiers and comments (<c>--</c> and nested <c>/* */</c>) are taken out, and what is left is split into tokens. A statement is refused when
/// a token is a write, DDL or administrative verb, a locking clause, or a call to a function that has side effects or reaches outside the database.
/// It is deliberately stricter than needed: a column that is literally named like a verb must be quoted. Anything it cannot read with certainty
/// (an unterminated literal or comment, a character outside printable ASCII in code, a unicode-escaped identifier) is refused too.
/// </summary>
internal static class WarehouseSqlPolicy
{
    private static readonly FrozenSet<string> Verbs = new[]
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "TRUNCATE", "GRANT", "REVOKE", "COPY", "CALL", "DO", "SET", "RESET",
        "LOCK", "VACUUM", "ANALYZE", "CLUSTER", "REINDEX", "REFRESH", "COMMENT", "SECURITY", "EXECUTE", "PREPARE", "DEALLOCATE", "LISTEN",
        "NOTIFY", "UNLISTEN", "DISCARD", "BEGIN", "COMMIT", "ROLLBACK", "SAVEPOINT", "INTO", "LOAD", "CHECKPOINT", "REASSIGN", "ABORT", "RELEASE", "IMPORT",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The word after FOR that makes a locking clause (FOR UPDATE, FOR NO KEY UPDATE, FOR SHARE, FOR KEY SHARE).</summary>
    private static readonly FrozenSet<string> LockingWords = new[] { "UPDATE", "SHARE", "NO", "KEY" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> FunctionNames = new[]
    {
        "set_config", "pg_terminate_backend", "pg_cancel_backend", "pg_reload_conf", "pg_rotate_logfile", "pg_switch_wal", "pg_notify", "nextval", "setval",
        "pg_read_file", "pg_read_binary_file", "pg_ls_dir", "pg_stat_file", "pg_sleep", "pg_export_snapshot", "pg_promote", "pg_logical_emit_message",
        "lo_import", "lo_export", "lo_create", "lo_creat", "lo_unlink", "lo_open", "lo_close", "lo_read", "lo_write", "lo_get", "lo_put", "lo_from_bytea",
        "lo_truncate", "lo_truncate64", "lo_tell", "lo_tell64", "lo_lseek", "lo_lseek64", "loread", "lowrite", "pg_file_write", "pg_file_unlink", "pg_file_rename",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Function name prefixes that are refused. <c>query_to_</c> and <c>cursor_to_</c> run SQL held in a string, so they could hide a statement that the scan
    /// cannot see, and the <c>pg_create_</c> and <c>pg_drop_</c> families change replication slots and the like.
    /// </summary>
    private static readonly string[] FunctionPrefixes =
    [
        "pg_create_", "pg_drop_", "pg_advisory_", "pg_try_advisory_", "pg_ls_", "pg_read_", "pg_replication_", "pg_logical_", "pg_backup_", "pg_wal_replay_",
        "pg_stat_reset", "pg_sleep", "pg_log_", "txid_", "dblink", "query_to_", "cursor_to_",
    ];

    /// <summary>Reads the text once. Always returns a result: a text that cannot be read safely is a violation, not an exception.</summary>
    internal static WarehouseSqlAnalysis Analyze(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        if (!Tokenize(sql, out List<Token> tokens, out string? unreadable))
        {
            return new WarehouseSqlAnalysis(unreadable, false);
        }

        return new WarehouseSqlAnalysis(FindViolation(tokens), tokens.Any(token => token.Kind == TokenKind.Parameter && token.Text.Equals("@provider_id", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The first rule the text breaks, or null when it passes.</summary>
    internal static string? FindViolation(string sql) => Analyze(sql).Violation;

    private static string? FindViolation(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Kind == TokenKind.Word && Verbs.Contains(token.Text))
            {
                return $"The query uses the write or administrative keyword {token.Text.ToUpperInvariant()}.";
            }

            if (token.Kind == TokenKind.Word && token.Text.Equals("FOR", StringComparison.OrdinalIgnoreCase)
                && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Word && LockingWords.Contains(tokens[i + 1].Text))
            {
                return "The query uses a row locking clause.";
            }

            bool isName = token.Kind is TokenKind.Word or TokenKind.QuotedIdentifier;
            bool isCall = i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Punctuation && tokens[i + 1].Text == "(";
            if (isName && isCall && IsDeniedFunction(token.Text))
            {
                return $"The query calls the function {token.Text.ToLowerInvariant()}, which is not allowed.";
            }
        }

        return null;
    }

    private static bool IsDeniedFunction(string name) =>
        FunctionNames.Contains(name) || FunctionPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) || IsLargeObjectFunction(name);

    // lo_ followed by letters only is a large object function. A column such as lo_x1 is not a call, so only the call form is matched, with the name check kept narrow.
    private static bool IsLargeObjectFunction(string name) =>
        name.StartsWith("lo_", StringComparison.OrdinalIgnoreCase) && name.Length > 3 && name.Skip(3).All(char.IsAsciiLetter);

    private enum TokenKind
    {
        Word,
        QuotedIdentifier,
        Parameter,
        Punctuation,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static bool Tokenize(string sql, out List<Token> tokens, out string? unreadable)
    {
        var scanner = new Scanner(sql);
        bool ok = scanner.Run();
        tokens = scanner.Tokens;
        unreadable = scanner.Problem;
        return ok;
    }

    /// <summary>One pass over the text. Each kind of thing PostgreSQL can meet has its own small method.</summary>
    private sealed class Scanner(string sql)
    {
        private int position;

        internal List<Token> Tokens { get; } = [];

        internal string? Problem { get; private set; }

        internal bool Run()
        {
            while (position < sql.Length && Problem is null)
            {
                Step();
            }

            return Problem is null;
        }

        private char Current => sql[position];

        private char? At(int offset) => position + offset >= 0 && position + offset < sql.Length ? sql[position + offset] : null;

        private void Fail(string problem) => Problem = problem;

        private void Step()
        {
            char c = Current;
            if (c == '\0')
            {
                Fail("The query contains a NUL character.");
            }
            else if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                position++;
            }
            else if (c == '-' && At(1) == '-')
            {
                int end = sql.IndexOfAny(['\n', '\r'], position);
                position = end < 0 ? sql.Length : end;
            }
            else if (c == '/' && At(1) == '*')
            {
                SkipComment();
            }
            else
            {
                StepInLiteralOrCode(c);
            }
        }

        private void StepInLiteralOrCode(char c)
        {
            if (c == '\'')
            {
                SkipString();
            }
            else if (c == '"')
            {
                ReadQuotedIdentifier();
            }
            else if (c == '$' && (position == 0 || !IsWordPart(sql[position - 1])) && TryDollarTag(sql, position, out string tag))
            {
                SkipDollarQuoted(tag);
            }
            else if (c == '@' && At(1) is { } afterAt && IsWordStart(afterAt))
            {
                ReadWord(TokenKind.Parameter);
            }
            else if (IsWordStart(c))
            {
                ReadWord(TokenKind.Word);
            }
            else if (c is < ' ' or > '~')
            {
                // PostgreSQL treats every non-ASCII character as part of an identifier, and control characters in odd ways, so none is accepted in code.
                Fail("The query contains a control or non-ASCII character outside a string, comment or quoted identifier.");
            }
            else
            {
                Tokens.Add(new Token(TokenKind.Punctuation, c.ToString()));
                position++;
            }
        }

        private void SkipComment()
        {
            if (SkipBlockComment(sql, position, out int end))
            {
                position = end;
                return;
            }

            Fail("The query has a comment that is never closed.");
        }

        private void SkipString()
        {
            bool escapeString = At(-1) is 'E' or 'e' && (At(-2) is not { } before || !IsWordPart(before));
            if (SkipQuoted(sql, position, '\'', escapeString, out int end, out _))
            {
                position = end;
                return;
            }

            Fail("The query has a string that is never closed.");
        }

        private void ReadQuotedIdentifier()
        {
            if (At(-1) == '&' && At(-2) is 'U' or 'u')
            {
                Fail("The query uses a unicode-escaped identifier, which is not allowed.");
                return;
            }

            if (!SkipQuoted(sql, position, '"', false, out int end, out string content))
            {
                Fail("The query has a quoted identifier that is never closed.");
                return;
            }

            Tokens.Add(new Token(TokenKind.QuotedIdentifier, content));
            position = end;
        }

        private void SkipDollarQuoted(string tag)
        {
            int close = sql.IndexOf(tag, position + tag.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                Fail("The query has a dollar-quoted string that is never closed.");
                return;
            }

            position = close + tag.Length;
        }

        private void ReadWord(TokenKind kind)
        {
            int end = position + 1;
            while (end < sql.Length && IsWordPart(sql[end]))
            {
                end++;
            }

            Tokens.Add(new Token(kind, sql[position..end]));
            position = end;
        }

        private static bool IsWordStart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

        private static bool IsWordPart(char c) => IsWordStart(c) || c == '$';


        /// <summary>Skips a block comment, which PostgreSQL lets nest. Returns false when it is not closed.</summary>
        private static bool SkipBlockComment(string sql, int start, out int end)
        {
            int depth = 0;
            int i = start;
            while (i < sql.Length - 1)
            {
                if (sql[i] == '/' && sql[i + 1] == '*')
                {
                    depth++;
                    i += 2;
                }
                else if (sql[i] == '*' && sql[i + 1] == '/')
                {
                    depth--;
                    i += 2;
                    if (depth == 0)
                    {
                        end = i;
                        return true;
                    }
                }
                else
                {
                    i++;
                }
            }

            end = sql.Length;
            return false;
        }

        /// <summary>Skips a quoted run that starts at <paramref name="start"/>, where the quote character is doubled to write it. Backslash escapes only in an <c>E''</c> string.</summary>
        private static bool SkipQuoted(string sql, int start, char quote, bool backslashEscapes, out int end, out string content)
        {
            var text = new StringBuilder();
            int i = start + 1;
            while (i < sql.Length)
            {
                char c = sql[i];
                if (backslashEscapes && c == '\\')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote)
                    {
                        text.Append(quote);
                        i += 2;
                        continue;
                    }

                    end = i + 1;
                    content = text.ToString();
                    return true;
                }

                text.Append(c);
                i++;
            }

            end = sql.Length;
            content = "";
            return false;
        }

        /// <summary>A dollar-quote opener is <c>$$</c> or <c>$tag$</c> where the tag starts with a letter or underscore. <c>$1</c> is a parameter and is not one.</summary>
        private static bool TryDollarTag(string sql, int start, out string tag)
        {
            tag = "";
            int i = start + 1;
            if (i < sql.Length && (char.IsAsciiLetter(sql[i]) || sql[i] == '_'))
            {
                while (i < sql.Length && (char.IsAsciiLetterOrDigit(sql[i]) || sql[i] == '_'))
                {
                    i++;
                }
            }

            if (i >= sql.Length || sql[i] != '$')
            {
                return false;
            }

            tag = sql[start..(i + 1)];
            return true;
        }
    }
}
