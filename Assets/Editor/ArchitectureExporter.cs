using UnityEngine;
using UnityEditor;
using System.Text;

public class ArchitectureExporter : Editor
{
    [MenuItem("GameObject/Copy Architecture for AI", false, 0)]
    static void CopyArchitecture()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null) 
        {
            Debug.LogWarning("Select a GameObject first!");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Architecture for: {selected.name}");
        BuildTree(selected.transform, "", sb);
        
        GUIUtility.systemCopyBuffer = sb.ToString();
        Debug.Log("Architecture copied to clipboard! You can now paste it directly into Gemini.");
    }

    static void BuildTree(Transform transform, string indent, StringBuilder sb)
    {
        sb.AppendLine($"{indent}- {transform.name}");
        
        // List all components attached to this specific GameObject
        Component[] components = transform.GetComponents<Component>();
        foreach (var comp in components)
        {
            // Skip the Transform component as every object has one, keeping the output clean
            if (comp != null && !(comp is Transform))
            {
                sb.AppendLine($"{indent}  [Component: {comp.GetType().Name}]");
            }
        }

        // Recursively do the same for all children
        foreach (Transform child in transform)
        {
            BuildTree(child, indent + "  ", sb);
        }
    }
}