using System.Collections.Generic;

namespace D365LabelCreator.Services;

/// <summary>
/// A minimal X++ source scanner. It walks source text and yields only double-quoted string
/// literals, skipping line comments (// and ///), block comments (/* */), single-quoted literals
/// ('...') and strings that appear inside attribute brackets ([Attr("...")]). This is what limits
/// the noise: single-quoted spans, comment text and attribute arguments never register.
/// </summary>
public static class XppLexer
{
    public readonly struct StringLiteral
    {
        /// <summary>Offset of the first character of the content (just after the opening quote).</summary>
        public int ContentStart { get; init; }
        /// <summary>Length of the content (between the quotes), excluding the quotes.</summary>
        public int ContentLength { get; init; }
        /// <summary>The literal content, exactly as written between the quotes.</summary>
        public string Content { get; init; }
    }

    /// <summary>Finds every double-quoted string literal in <paramref name="src"/> that is not inside an attribute.</summary>
    public static List<StringLiteral> FindDoubleQuotedStrings(string src)
    {
        var result = new List<StringLiteral>();
        int i = 0;
        int n = src.Length;

        char prevSig = '\0';          // last significant (non-space, non-comment) char
        var bracketStack = new List<bool>(); // one entry per open '[': true = attribute bracket
        int attrDepth = 0;            // number of open attribute brackets

        while (i < n)
        {
            char c = src[i];

            // Whitespace: does not change prevSig.
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                i++;
                continue;
            }

            // Line comment: // or /// — does not change prevSig.
            if (c == '/' && i + 1 < n && src[i + 1] == '/')
            {
                i += 2;
                while (i < n && src[i] != '\n')
                    i++;
                continue;
            }

            // Block comment: /* ... */ — does not change prevSig.
            if (c == '/' && i + 1 < n && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/'))
                    i++;
                i += 2;
                if (i > n) i = n;
                continue;
            }

            // Single-quoted literal: skipped wholesale; counts as a value.
            if (c == '\'')
            {
                i++;
                while (i < n && src[i] != '\'')
                {
                    if (src[i] == '\\' && i + 1 < n)
                        i++;
                    i++;
                }
                i++;
                prevSig = '\'';
                continue;
            }

            // Double-quoted literal: captured unless inside an attribute bracket; counts as a value.
            if (c == '"')
            {
                int contentStart = i + 1;
                int j = contentStart;
                while (j < n && src[j] != '"')
                {
                    if (src[j] == '\\' && j + 1 < n)
                        j++;
                    j++;
                }
                int contentLen = j - contentStart;
                if (attrDepth == 0)
                {
                    result.Add(new StringLiteral
                    {
                        ContentStart = contentStart,
                        ContentLength = contentLen,
                        Content = src.Substring(contentStart, contentLen),
                    });
                }
                i = j + 1;
                prevSig = '"';
                continue;
            }

            // Opening bracket: attribute if the previous value-like char is absent.
            if (c == '[')
            {
                bool isAttr = !IsValueEnd(prevSig);
                bracketStack.Add(isAttr);
                if (isAttr) attrDepth++;
                prevSig = '[';
                i++;
                continue;
            }

            if (c == ']')
            {
                if (bracketStack.Count > 0)
                {
                    bool wasAttr = bracketStack[^1];
                    bracketStack.RemoveAt(bracketStack.Count - 1);
                    if (wasAttr) attrDepth--;
                }
                prevSig = ']';
                i++;
                continue;
            }

            prevSig = c;
            i++;
        }

        return result;
    }

    /// <summary>True if <paramref name="c"/> is a character an expression/value ends with.</summary>
    private static bool IsValueEnd(char c) =>
        c == ')' || c == ']' || c == '"' || c == '\'' ||
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
}
