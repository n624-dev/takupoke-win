using System.Text;
using System.Xml;
using System.Xml.Linq;
using Takupoke.Core;

namespace Takupoke.Infrastructure.Parsing;

public static class BoundedXml
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    public static XElement Read(byte[] bytes, string rootName, string namespaceName, CancellationToken cancellationToken = default)
    {
        if (bytes.Length > MaximumBytes) throw new ChangeParseException(ChangeErrorCode.Limit);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new ChangeParseException(ChangeErrorCode.Unsupported); }
        if (text.Contains('\0') || text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
            throw new ChangeParseException(ChangeErrorCode.Unsupported);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes };
        try
        {
            using (var reader = XmlReader.Create(new StringReader(text), settings))
            {
                var count = 0;
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.Element && (++count > 200_000 || reader.Depth >= 64)) throw new ChangeParseException(ChangeErrorCode.Limit);
                }
            }
            using var secondReader = XmlReader.Create(new StringReader(text), settings);
            var root = XDocument.Load(secondReader).Root;
            if (root?.Name != XName.Get(rootName, namespaceName)) throw new ChangeParseException(ChangeErrorCode.InvalidXml);
            var singletons = new HashSet<string> { "workbookPr", "sheets", "sheetData", "v", "is", "f", "mergeCells" };
            foreach (var node in root.DescendantsAndSelf())
            {
                if (node.Elements().Where(n => n.Name.Namespace == node.Name.Namespace && singletons.Contains(n.Name.LocalName))
                    .GroupBy(n => n.Name).Any(group => group.Count() > 1)) throw new ChangeParseException(ChangeErrorCode.InvalidXml);
                if (node.Name.LocalName is "t" or "v" && Encoding.UTF8.GetByteCount(node.Value) > 4096) throw new ChangeParseException(ChangeErrorCode.Limit);
            }
            return root;
        }
        catch (XmlException) { throw new ChangeParseException(ChangeErrorCode.InvalidXml); }
    }
}
