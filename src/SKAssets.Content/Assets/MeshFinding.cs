namespace SKAssets.Content.Assets
{
    /// <summary>How much a finding matters.</summary>
    public enum FindingSeverity
    {
        /// <summary>Worth knowing, and normal in some content.</summary>
        Note,

        /// <summary>The file is unusual in a way that is usually a mistake.</summary>
        Warning,

        /// <summary>The file cannot do the job the record gives it.</summary>
        Error,
    }

    /// <summary>
    /// Something about a mesh that does not fit what names it.
    /// </summary>
    /// <param name="Rule">A stable identifier, so a report can be filtered or suppressed by rule.</param>
    /// <param name="Severity">How much it matters.</param>
    /// <param name="Message">What is wrong, in a sentence.</param>
    public sealed record MeshFinding(string Rule, FindingSeverity Severity, string Message)
    {
        /// <inheritdoc />
        public override string ToString() => $"[{Severity}] {Rule}: {Message}";
    }
}
