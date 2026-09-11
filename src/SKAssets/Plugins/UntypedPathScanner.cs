using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;

namespace SKAssets.Plugins
{
    /// <summary>
    /// Walks every field of a record looking for strings shaped like filenames.
    /// </summary>
    /// <remarks>
    /// This is the answer to "what if a plugin puts a path somewhere we do not know
    /// about". It reflects over the whole record rather than reading named fields,
    /// so it finds what <see cref="UntypedPathFields"/> has not been told about --
    /// which is how that list was built in the first place.
    ///
    /// It is slow, it is noisy, and both are inherent. Reflecting over every
    /// property of every record costs around ten times a link sweep, and a string
    /// that looks like a filename is not always one: the masters offer node names
    /// spelled <c>BASE Meshes\...\skeleton.nif</c> and several hundred Creation Kit
    /// filter folders. Treat what comes out as candidates to read, not as assets.
    /// </remarks>
    internal static class UntypedPathScanner
    {
        /// <summary>
        /// A filename: something, a dot, and a short extension. Anything ending in a
        /// separator is a folder and is dropped here rather than downstream, which is
        /// what keeps <c>Quest.Filter</c> out of the results.
        /// </summary>
        private static readonly Regex FileShaped =
            new(@"^[^\r\n\t]{1,255}\.[A-Za-z0-9]{2,5}$", RegexOptions.Compiled);

        /// <summary>
        /// How deep to follow a record's own sub-objects. Six is past the deepest
        /// nesting the Skyrim schema has; the limit is there for cycles, not depth.
        /// </summary>
        private const int MaxDepth = 6;

        internal static IEnumerable<(string Field, string Value)> Scan(IMajorRecordGetter record)
        {
            var found = new List<(string, string)>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

            Walk(record, record.GetType().Name, 0, found, visited);

            return found;
        }

        private static void Walk(object? value, string field, int depth, List<(string, string)> found, HashSet<object> visited)
        {
            if (value is null || depth > MaxDepth)
                return;

            switch (value)
            {
                case string text:
                    if (FileShaped.IsMatch(text))
                        found.Add((field, text));
                    return;

                // Asset links are the sweep's first pass; a record's own links would
                // otherwise come back a second time under a different origin.
                case IAssetLinkGetter:
                case IFormLinkGetter:
                    return;

                // Localised text is not a path, and resolving one reaches for the
                // strings folder -- an expensive lookup here, and a failing one where
                // the game is not installed.
                case ITranslatedStringGetter:
                    return;

                // A record inside this one -- a placed object, a dialogue response.
                // The sweep reaches it separately, and walking in from here would
                // report everything in a cell as the cell's.
                case IMajorRecordGetter when depth > 0:
                    return;

                case IEnumerable items and not IDictionary:
                    foreach (object? item in items)
                        Walk(item, field + "[]", depth + 1, found, visited);
                    return;
            }

            var type = value.GetType();

            if (type.IsPrimitive || type.IsEnum || value is decimal or DateTime or Guid)
                return;

            // Anything that is not Mutagen's is not part of the record.
            if (type.Namespace?.StartsWith("Mutagen", StringComparison.Ordinal) != true)
                return;

            if (!visited.Add(value))
                return;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                    continue;

                // Identity and schema plumbing: never a path, and Registration walks
                // into the whole type system if followed.
                if (property.Name is "Registration" or "StaticRegistration" or "FormKey"
                    or "FormVersion" or "ModKey" or "EditorID")
                    continue;

                object? child;
                try
                {
                    child = property.GetValue(value);
                }
                catch
                {
                    // A subrecord the file does not actually carry. Nothing to read.
                    continue;
                }

                Walk(child, $"{field}.{property.Name}", depth + 1, found, visited);
            }
        }
    }
}
