using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Polymaxcube.MetadataRecursiveLister.Editor
{
    /// <summary>
    /// Lists every node under a selected root (recursively) and exports Metadata
    /// properties to JSON. Output folder is configurable.
    /// Menu: Tools → Metadata Recursive Lister
    /// </summary>
    public class MetadataRecursiveLister : EditorWindow
    {
        private GameObject rootObject;
        private string outputFolder = "";
        private bool includeNodesWithoutMetadata = true;
        private bool writeIndividualFiles = false;
        private Vector2 previewScroll;
        private string lastPreview = "";
        private int lastNodeCount;
        private int lastMetadataCount;

        private const string IconPath =
            "Packages/com.polymaxcube.metadatarecursivelister/Editor/Icons/MetadataListerIcon.png";

        private const int ContentPaddingLeft = 14;
        private const int ContentPaddingRight = 14;
        private const int ContentPaddingTop = 10;
        private const int ContentPaddingBottom = 12;

        private GUIStyle contentPaddingStyle;

        [MenuItem("Tools/Metadata Recursive Lister")]
        public static void ShowWindow()
        {
            var window = GetWindow<MetadataRecursiveLister>();
            window.minSize = new Vector2(420, 360);
            window.ApplyWindowIcon();
        }

        private void OnEnable()
        {
            ApplyWindowIcon();

            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = Path.Combine(Application.dataPath, "CAD_Output", "Metadata");

            if (rootObject == null && Selection.activeGameObject != null)
                rootObject = Selection.activeGameObject;
        }

        private void ApplyWindowIcon()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            titleContent = icon != null
                ? new GUIContent("Metadata Lister", icon)
                : new GUIContent("Metadata Lister");
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void EnsureStyles()
        {
            if (contentPaddingStyle != null)
                return;

            contentPaddingStyle = new GUIStyle
            {
                padding = new RectOffset(
                    ContentPaddingLeft,
                    ContentPaddingRight,
                    ContentPaddingTop,
                    ContentPaddingBottom)
            };
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.BeginVertical(contentPaddingStyle);

            GUILayout.Label("Recursive Metadata Lister", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Walks the full hierarchy under the root, collects Metadata on every node, " +
                "and writes a combined JSON to the output folder.",
                MessageType.Info);

            EditorGUILayout.Space(10);

            rootObject = (GameObject)EditorGUILayout.ObjectField(
                "Root GameObject",
                rootObject,
                typeof(GameObject),
                true);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Use Current Selection", GUILayout.Height(22)))
            {
                if (Selection.activeGameObject != null)
                    rootObject = Selection.activeGameObject;
                else
                    EditorUtility.DisplayDialog("No Selection", "Select a GameObject in the Scene or Hierarchy.", "OK");
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Output Folder");
            outputFolder = EditorGUILayout.TextField(outputFolder);
            if (GUILayout.Button("Browse…", GUILayout.Width(80), GUILayout.Height(20)))
            {
                string picked = EditorUtility.OpenFolderPanel(
                    "Choose Metadata Output Folder",
                    string.IsNullOrEmpty(outputFolder) ? Application.dataPath : outputFolder,
                    "");
                if (!string.IsNullOrEmpty(picked))
                    outputFolder = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            includeNodesWithoutMetadata = EditorGUILayout.Toggle(
                new GUIContent(
                    "Include Nodes Without Metadata",
                    "When enabled, every hierarchy node is listed. When disabled, only nodes with a Metadata component are exported."),
                includeNodesWithoutMetadata);

            EditorGUILayout.Space(2);
            writeIndividualFiles = EditorGUILayout.Toggle(
                new GUIContent(
                    "Write Individual Files",
                    "Also write one JSON file per node (in addition to the combined file)."),
                writeIndividualFiles);

            EditorGUILayout.Space(14);

            EditorGUI.BeginDisabledGroup(rootObject == null || string.IsNullOrEmpty(outputFolder));
            if (GUILayout.Button("List & Export All Metadata", GUILayout.Height(36)))
            {
                Export(rootObject, outputFolder, includeNodesWithoutMetadata, writeIndividualFiles);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Preview Only (Console + Window)", GUILayout.Height(28)))
            {
                if (rootObject == null)
                {
                    EditorUtility.DisplayDialog("Error", "Assign a Root GameObject first.", "OK");
                }
                else
                {
                    var nodes = CollectNodes(rootObject, includeNodesWithoutMetadata);
                    lastNodeCount = nodes.Count;
                    lastMetadataCount = 0;
                    foreach (var n in nodes)
                    {
                        if (n.hasMetadata)
                            lastMetadataCount++;
                    }

                    lastPreview = BuildPreviewText(rootObject.name, nodes);
                    Debug.Log(
                        $"[Metadata Lister] Preview '{rootObject.name}': {lastNodeCount} node(s), " +
                        $"{lastMetadataCount} with Metadata.\n{lastPreview}");
                }
            }

            if (!string.IsNullOrEmpty(lastPreview))
            {
                EditorGUILayout.Space(12);
                GUILayout.Label(
                    $"Last preview — nodes: {lastNodeCount}, with Metadata: {lastMetadataCount}",
                    EditorStyles.miniBoldLabel);
                EditorGUILayout.Space(4);
                previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.MinHeight(120));
                EditorGUILayout.TextArea(lastPreview, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        public static int Export(
            GameObject root,
            string outputFolder,
            bool includeNodesWithoutMetadata = true,
            bool writeIndividualFiles = false)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(outputFolder))
                throw new ArgumentException("Output folder is required.", nameof(outputFolder));

            Directory.CreateDirectory(outputFolder);

            var nodes = CollectNodes(root, includeNodesWithoutMetadata);
            var usedFileNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int metadataCount = 0;

            if (writeIndividualFiles)
            {
                foreach (var node in nodes)
                {
                    if (!node.hasMetadata && !includeNodesWithoutMetadata)
                        continue;

                    string cleanName = SanitizeFileName(node.cadPartName);
                    string uniqueName = MakeUniqueFileName(cleanName, usedFileNames);
                    string path = Path.Combine(outputFolder, uniqueName + "_metadata.json");
                    File.WriteAllText(path, ConvertNodeToJson(node));
                }
            }

            foreach (var node in nodes)
            {
                if (node.hasMetadata)
                    metadataCount++;
            }

            string combinedPath = Path.Combine(
                outputFolder,
                SanitizeFileName(root.name) + "_all_nodes_metadata.json");
            File.WriteAllText(combinedPath, ConvertNodesToCombinedJson(root.name, nodes));

            AssetDatabase.Refresh();

            Debug.Log(
                $"[Metadata Lister] Exported {nodes.Count} node(s) " +
                $"({metadataCount} with Metadata) under '{root.name}' → {combinedPath}");

            EditorUtility.DisplayDialog(
                "Metadata Export Complete",
                $"Nodes listed: {nodes.Count}\n" +
                $"With Metadata: {metadataCount}\n\n" +
                $"Combined file:\n{combinedPath}",
                "OK");

            return nodes.Count;
        }

        private static List<NodeRecord> CollectNodes(GameObject root, bool includeNodesWithoutMetadata)
        {
            var nodes = new List<NodeRecord>();

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Component metadata = child.GetComponent("Metadata");
                Dictionary<string, string> properties = null;
                bool hasMetadata = metadata != null;

                if (hasMetadata)
                {
                    MethodInfo method = metadata.GetType().GetMethod(
                        "getProperties",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (method != null)
                        properties = method.Invoke(metadata, null) as Dictionary<string, string>;

                    if (properties == null)
                        properties = new Dictionary<string, string>();
                }
                else if (!includeNodesWithoutMetadata)
                {
                    continue;
                }
                else
                {
                    properties = new Dictionary<string, string>();
                }

                nodes.Add(new NodeRecord
                {
                    cadPartName = child.name,
                    hierarchyPath = GetHierarchyPath(child, root.transform),
                    instanceId = child.GetEntityId(),
                    depth = GetDepth(child, root.transform),
                    hasMetadata = hasMetadata,
                    activeSelf = child.gameObject.activeSelf,
                    activeInHierarchy = child.gameObject.activeInHierarchy,
                    childCount = child.childCount,
                    properties = properties
                });
            }

            return nodes;
        }

        private static string BuildPreviewText(string rootName, List<NodeRecord> nodes)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Root: {rootName}");
            sb.AppendLine($"Node count: {nodes.Count}");
            sb.AppendLine("---");

            int limit = Math.Min(nodes.Count, 200);
            for (int i = 0; i < limit; i++)
            {
                var n = nodes[i];
                string flag = n.hasMetadata ? $"meta({n.properties.Count})" : "no-meta";
                sb.AppendLine($"[{n.depth}] {n.hierarchyPath}  [{flag}]");
            }

            if (nodes.Count > limit)
                sb.AppendLine($"... and {nodes.Count - limit} more");

            return sb.ToString();
        }

        private static string GetHierarchyPath(Transform node, Transform root)
        {
            var parts = new List<string>();
            Transform current = node;
            while (current != null)
            {
                parts.Add(current.name);
                if (current == root)
                    break;
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static int GetDepth(Transform node, Transform root)
        {
            int depth = 0;
            Transform current = node;
            while (current != null && current != root)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        private static string MakeUniqueFileName(string baseName, Dictionary<string, int> usedFileNames)
        {
            if (!usedFileNames.TryGetValue(baseName, out int count))
            {
                usedFileNames[baseName] = 1;
                return baseName;
            }

            count++;
            usedFileNames[baseName] = count;
            return $"{baseName}_{count}";
        }

        private static string ConvertNodeToJson(NodeRecord node)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"cadPartName\": \"{EscapeJson(node.cadPartName)}\",");
            sb.AppendLine($"  \"hierarchyPath\": \"{EscapeJson(node.hierarchyPath)}\",");
            sb.AppendLine($"  \"instanceId\": {node.instanceId},");
            sb.AppendLine($"  \"depth\": {node.depth},");
            sb.AppendLine($"  \"hasMetadata\": {(node.hasMetadata ? "true" : "false")},");
            sb.AppendLine($"  \"activeSelf\": {(node.activeSelf ? "true" : "false")},");
            sb.AppendLine($"  \"activeInHierarchy\": {(node.activeInHierarchy ? "true" : "false")},");
            sb.AppendLine($"  \"childCount\": {node.childCount},");
            AppendProperties(sb, node.properties, "  ");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ConvertNodesToCombinedJson(string rootName, List<NodeRecord> nodes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"rootName\": \"{EscapeJson(rootName)}\",");
            sb.AppendLine($"  \"nodeCount\": {nodes.Count},");
            sb.AppendLine($"  \"exportedAt\": \"{EscapeJson(DateTime.Now.ToString("o"))}\",");
            sb.AppendLine("  \"nodes\": [");

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"cadPartName\": \"{EscapeJson(node.cadPartName)}\",");
                sb.AppendLine($"      \"hierarchyPath\": \"{EscapeJson(node.hierarchyPath)}\",");
                sb.AppendLine($"      \"instanceId\": {node.instanceId},");
                sb.AppendLine($"      \"depth\": {node.depth},");
                sb.AppendLine($"      \"hasMetadata\": {(node.hasMetadata ? "true" : "false")},");
                sb.AppendLine($"      \"activeSelf\": {(node.activeSelf ? "true" : "false")},");
                sb.AppendLine($"      \"activeInHierarchy\": {(node.activeInHierarchy ? "true" : "false")},");
                sb.AppendLine($"      \"childCount\": {node.childCount},");
                AppendProperties(sb, node.properties, "      ");
                sb.Append("    }");
                sb.AppendLine(i < nodes.Count - 1 ? "," : "");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendProperties(
            StringBuilder sb,
            Dictionary<string, string> properties,
            string indent)
        {
            sb.AppendLine($"{indent}\"properties\": {{");

            if (properties == null || properties.Count == 0)
            {
                sb.AppendLine($"{indent}}}");
                return;
            }

            int index = 0;
            foreach (var kvp in properties)
            {
                index++;
                string comma = index < properties.Count ? "," : "";
                sb.AppendLine(
                    $"{indent}  \"{EscapeJson(kvp.Key)}\": \"{EscapeJson(kvp.Value)}\"{comma}");
            }

            sb.AppendLine($"{indent}}}");
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", " ")
                .Replace("\r", "");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private class NodeRecord
        {
            public string cadPartName;
            public string hierarchyPath;
            public EntityId instanceId;
            public int depth;
            public bool hasMetadata;
            public bool activeSelf;
            public bool activeInHierarchy;
            public int childCount;
            public Dictionary<string, string> properties;
        }
    }
}
