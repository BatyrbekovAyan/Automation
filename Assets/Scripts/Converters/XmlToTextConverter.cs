using System.Text;
using System.Xml;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

public static class XmlToTextConverter
{
    public static string ConvertXmlToText(string xmlContent)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xmlContent);

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

        // Leaf node with text
        if (node.ChildNodes.Count == 1 &&
            node.FirstChild.NodeType == XmlNodeType.Text)
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