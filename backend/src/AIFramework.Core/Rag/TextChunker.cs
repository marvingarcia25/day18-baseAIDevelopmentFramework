namespace AIFramework.Core.Rag;

/// <summary>
/// Splits documents into chunks for embedding. Strategy: pack whole paragraphs up to
/// <see cref="MaxChunkChars"/>, carrying <see cref="OverlapChars"/> of trailing context into
/// the next chunk so facts that straddle a boundary stay retrievable.
/// Character counts (not tokens) keep this dependency-free; ~4 chars ≈ 1 token for English.
/// </summary>
public sealed class TextChunker(int maxChunkChars = 1500, int? overlapChars = null)
{
    public int MaxChunkChars { get; } = maxChunkChars > 0
        ? maxChunkChars
        : throw new ArgumentOutOfRangeException(nameof(maxChunkChars));

    /// <summary>Defaults to ~13% of the chunk size when not set explicitly.</summary>
    public int OverlapChars { get; } = overlapChars switch
    {
        null => maxChunkChars / 8,
        >= 0 when overlapChars < maxChunkChars => overlapChars.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(overlapChars)),
    };

    public IReadOnlyList<string> Chunk(string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        if (text.Length == 0)
        {
            return [];
        }
        if (text.Length <= MaxChunkChars)
        {
            return [text];
        }

        var paragraphs = text
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitOversized);

        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > MaxChunkChars)
            {
                chunks.Add(current.ToString());
                var overlap = TailForOverlap(current.ToString());
                current.Clear();
                current.Append(overlap);
            }
            if (current.Length > 0)
            {
                current.Append("\n\n");
            }
            current.Append(paragraph);
        }
        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }
        return chunks;
    }

    /// <summary>Paragraphs longer than a whole chunk are split on sentence-ish boundaries.</summary>
    private IEnumerable<string> SplitOversized(string paragraph)
    {
        if (paragraph.Length <= MaxChunkChars)
        {
            yield return paragraph;
            yield break;
        }

        var start = 0;
        while (start < paragraph.Length)
        {
            var length = Math.Min(MaxChunkChars, paragraph.Length - start);
            if (start + length < paragraph.Length)
            {
                var window = paragraph.Substring(start, length);
                var breakAt = window.LastIndexOfAny(['.', '!', '?', '\n', ' ']);
                if (breakAt > MaxChunkChars / 2)
                {
                    length = breakAt + 1;
                }
            }
            yield return paragraph.Substring(start, length).Trim();
            start += length;
        }
    }

    private string TailForOverlap(string chunk) =>
        OverlapChars == 0 || chunk.Length <= OverlapChars ? "" : chunk[^OverlapChars..];
}
