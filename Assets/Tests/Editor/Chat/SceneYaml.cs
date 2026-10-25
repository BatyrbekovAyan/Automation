using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A scene read as raw YAML documents keyed by local fileID — enough to walk GameObjects, their components and the
/// RectTransform tree, and to read a component's top-level fields — so a premise test never opens Main.unity (which would
/// wake every [ExecuteInEditMode] component in it). Shared by the builders' premise tests (AutoChipLearnLayerPremiseTests,
/// PersonalChatsBuilderPremiseTests); <see cref="ScriptGuid"/> resolves a component's script GUID from its type.
/// </summary>
internal sealed class SceneYaml
{
    private const string MonoBehaviourClass = "114";
    private const string RectTransformClass = "224";

    private static readonly Dictionary<Type, string> Guids = new Dictionary<Type, string>();

    private readonly Dictionary<string, string> docs = new Dictionary<string, string>();
    private readonly Dictionary<string, string> classes = new Dictionary<string, string>();

    /// <summary>A component's script GUID as the scene stores it in m_Script — resolved from the type, never typed in.</summary>
    public static string ScriptGuid(Type type)
    {
        if (Guids.TryGetValue(type, out string known)) return known;
        MonoScript script = MonoImporter.GetAllRuntimeMonoScripts().FirstOrDefault(s => s != null && s.GetClass() == type);
        Assert.IsNotNull(script, $"no MonoScript for {type.Name}");
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
        Assert.IsNotEmpty(guid, $"no GUID for {type.Name}'s script");
        Guids[type] = guid;
        return guid;
    }

    public static SceneYaml Load(string path)
    {
        var yaml = new SceneYaml();
        string text = File.ReadAllText(path);
        MatchCollection headers = Regex.Matches(text, @"^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$", RegexOptions.Multiline);
        for (int i = 0; i < headers.Count; i++)
        {
            int start = headers[i].Index + headers[i].Length;
            int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            string id = headers[i].Groups[2].Value;
            yaml.docs[id] = text.Substring(start, end - start);
            yaml.classes[id] = headers[i].Groups[1].Value;
        }
        Assert.Greater(yaml.docs.Count, 1000, $"{path} did not parse — every assertion below would be a false green");
        return yaml;
    }

    public string Single(string scriptGuid, string what)
    {
        string[] hits = docs.Keys.Where(id => classes[id] == MonoBehaviourClass && ScriptGuidOf(id) == scriptGuid).ToArray();
        Assert.AreEqual(1, hits.Length, $"exactly one {what} in the scene");
        return hits[0];
    }

    /// <summary>A top-level field's raw value (two-space indent = the document's own field, never a nested one).</summary>
    public string Field(string id, string name)
    {
        Assert.IsTrue(id != null && docs.ContainsKey(id), $"no document {id} in the scene");
        Match match = Regex.Match(docs[id], $@"^  {Regex.Escape(name)}: (.*?)\s*$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The local fileID a reference field points at; null when it is unset ({fileID: 0}) or absent.</summary>
    public string Ref(string id, string name)
    {
        string value = Field(id, name);
        Match match = value != null ? Regex.Match(value, @"^\{fileID: (-?\d+)\}$") : Match.Empty;
        return match.Success && match.Groups[1].Value != "0" ? match.Groups[1].Value : null;
    }

    /// <summary>A string field as the label shows it: the YAML quotes and \u escapes undone.</summary>
    public string Text(string id, string name)
    {
        string raw = Field(id, name) ?? "";
        return raw.StartsWith("\"") && raw.EndsWith("\"") ? Regex.Unescape(raw.Substring(1, raw.Length - 2)) : raw;
    }

    public string FontGuid(string tmpId) => AssetGuidIn(Field(tmpId, "m_fontAsset"));

    public string SpriteGuid(string imageId) => AssetGuidIn(Field(imageId, "m_Sprite"));

    public string ScriptGuidOf(string id)
    {
        Match match = Regex.Match(docs[id], @"^  m_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d+\}", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    public string GameObjectOf(string componentId) => Ref(componentId, "m_GameObject");

    public string NameOf(string gameObjectId) => Field(gameObjectId, "m_Name");

    public bool IsActive(string gameObjectId) => Field(gameObjectId, "m_IsActive") == "1";

    public string RectOf(string gameObjectId) => Components(gameObjectId).FirstOrDefault(id => classes[id] == RectTransformClass);

    public string Component(string gameObjectId, string scriptGuid) =>
        Components(gameObjectId).FirstOrDefault(id => classes[id] == MonoBehaviourClass && ScriptGuidOf(id) == scriptGuid);

    /// <summary>The GameObject named <paramref name="name"/> in <paramref name="rootGameObject"/>'s subtree (exactly one).</summary>
    public string Named(string rootGameObject, string name)
    {
        string[] hits = Subtree(RectOf(rootGameObject)).Select(GameObjectOf).Where(go => NameOf(go) == name).ToArray();
        Assert.AreEqual(1, hits.Length, $"exactly one «{name}» under {NameOf(rootGameObject)}");
        return hits[0];
    }

    public string Parent(string rectId) => Ref(rectId, "m_Father");

    public IEnumerable<string> Children(string rectId)
    {
        string body = docs[rectId];
        int start = body.IndexOf("  m_Children:", StringComparison.Ordinal);
        int end = body.IndexOf("  m_Father:", StringComparison.Ordinal);
        if (start < 0 || end < start) yield break;
        foreach (Match match in Regex.Matches(body.Substring(start, end - start), @"\{fileID: (-?\d+)\}"))
            yield return match.Groups[1].Value;
    }

    public IEnumerable<string> Subtree(string rectId)
    {
        foreach (string child in Children(rectId))
        {
            yield return child;
            foreach (string below in Subtree(child)) yield return below;
        }
    }

    public Vector2 Vec(string id, string name)
    {
        Match match = Regex.Match(Field(id, name) ?? "", @"^\{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)\}$");
        Assert.IsTrue(match.Success, $"{name} of document {id} is not a vector: '{Field(id, name)}'");
        return new Vector2(ParseFloat(match.Groups[1].Value), ParseFloat(match.Groups[2].Value));
    }

    public float Float(string id, string name)
    {
        string value = Field(id, name);
        Assert.IsNotNull(value, $"{name} missing on document {id}");
        return ParseFloat(value);
    }

    private static string AssetGuidIn(string reference)
    {
        Match match = Regex.Match(reference ?? "", @"guid: ([0-9a-f]{32})");
        return match.Success ? match.Groups[1].Value : null;
    }

    private IEnumerable<string> Components(string gameObjectId)
    {
        foreach (Match match in Regex.Matches(docs[gameObjectId], @"- component: \{fileID: (-?\d+)\}"))
            yield return match.Groups[1].Value;
    }

    private static float ParseFloat(string value) => float.Parse(value, CultureInfo.InvariantCulture);
}
