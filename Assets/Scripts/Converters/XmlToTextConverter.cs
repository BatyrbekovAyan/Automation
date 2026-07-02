using System.IO;
using System.Text;
using System.Xml;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

public static class XmlToTextConverter
{
    static XmlToTextConverter()
    {
        // XmlDocument.Load needs the codepage provider to honor declarations
        // like <?xml encoding="windows-1251"?> (typical for 1C/CommerceML).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Preferred entry point: decodes per the XML prolog's declared encoding
    /// (1C/CommerceML price exports are commonly windows-1251 — forcing UTF-8
    /// on those produced mojibake or a parse failure).
    /// </summary>
    public static string ConvertXmlToText(byte[] xmlBytes)
    {
        var doc = new XmlDocument();
        using var stream = new MemoryStream(xmlBytes);
        doc.Load(stream);
        return ConvertDocument(doc);
    }

    public static string ConvertXmlToText(string xmlContent)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xmlContent);
        return ConvertDocument(doc);
    }

    private static string ConvertDocument(XmlDocument doc)
    {
        var sb = new StringBuilder();

        foreach (XmlNode child in doc.DocumentElement.ChildNodes)
        {
            Traverse(child, sb, new List<string>());
        }

        return sb.ToString().Trim();
    }

    private static void Traverse(
        XmlNode node,
        StringBuilder sb,
        List<string> path)
    {
        if (node == null)
            return;

        // Count how many siblings with the same name exist at this level
        int siblingCount = node.ParentNode?
            .ChildNodes
            .OfType<XmlNode>()
            .Count(n => n.Name == node.Name) ?? 1;

        // Count index of this node among same-name siblings
        int index = 1;
        if (siblingCount > 1)
        {
            foreach (XmlNode sibling in node.ParentNode.ChildNodes)
            {
                if (sibling == node) break;
                if (sibling.Name == node.Name) index++;
            }
        }

        // Build path element
        string nodeLabel = siblingCount > 1
            ? $"{node.Name} [{index}]"
            : node.Name;

        var currentPath = new List<string>(path) { nodeLabel };

        // Attribute-carried data must not be invisible to retrieval — Kaspi
        // price XML puts sku/model on <offer ...> attributes, for example.
        if (node.Attributes != null)
        {
            foreach (XmlAttribute attribute in node.Attributes)
            {
                string attributeValue = attribute.Value?.Trim();
                if (!string.IsNullOrEmpty(attributeValue))
                    sb.Append($"{HumanizePath(currentPath)} {Humanize(attribute.Name)}: {attributeValue};\n");
            }
        }

        // Leaf node with text (CDATA included — descriptions are often CDATA-wrapped)
        if (node.ChildNodes.Count == 1 &&
            (node.FirstChild.NodeType == XmlNodeType.Text ||
             node.FirstChild.NodeType == XmlNodeType.CDATA))
        {
            string label = HumanizePath(currentPath);
            string value = node.InnerText.Trim();

            if (!string.IsNullOrEmpty(value))
            {
                sb.Append($"{label}: {value};\n");
            }

            return;
        }

        // Traverse children
        foreach (XmlNode child in node.ChildNodes)
        {
            Traverse(child, sb, currentPath);
        }
    }

    private static string HumanizePath(List<string> path)
    {
        return string.Join(" ", path.Select(Humanize));
    }

    private static string Humanize(string name)
    {
        name = name.Replace("_", " ").Replace("-", " ");
        name = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        return name.ToLowerInvariant().Trim();
    }
}