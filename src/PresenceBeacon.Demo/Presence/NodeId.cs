namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// The identity of one participant that publishes a beacon. In the demo this is a
/// sensor node such as <c>ridge-01</c>.
/// </summary>
/// <remarks>
/// The id is also the file name of that participant's beacon, so it must be safe to
/// place in a path. Validation lives in <see cref="Parse(string)"/> rather than in the
/// constructor so callers get one obvious place to look when an id is rejected.
/// </remarks>
public readonly record struct NodeId
{
    private const int MaxLength = 64;

    private readonly string? _value;

    private NodeId(string value) => _value = value;

    /// <summary>The raw identifier text.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// Builds a <see cref="NodeId"/> from text, rejecting anything that would not make a
    /// usable file name.
    /// </summary>
    /// <param name="value">Candidate identifier.</param>
    /// <returns>The validated identifier.</returns>
    public static NodeId Parse(string value)
    {
        if (!TryParse(value, out var id))
        {
            throw new ArgumentException(
                $"'{value}' is not a usable node id. Use 1 to {MaxLength} characters drawn from a-z, A-Z, 0-9, '-' and '_'.",
                nameof(value));
        }

        return id;
    }

    /// <summary>
    /// Non-throwing counterpart to <see cref="Parse(string)"/>.
    /// </summary>
    /// <param name="value">Candidate identifier.</param>
    /// <param name="id">The validated identifier when parsing succeeds.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a usable id.</returns>
    public static bool TryParse(string? value, out NodeId id)
    {
        id = default;

        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsAllowed(character))
            {
                return false;
            }
        }

        id = new NodeId(value);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    // Deliberately narrower than the filesystem allows. Dots, spaces and separators are
    // all legal in a file name somewhere, and every one of them is a way for an id to
    // escape its directory or collide with a temp file.
    private static bool IsAllowed(char character) =>
        character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_';
}
