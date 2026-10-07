using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pets are drawn at runtime by Vector Graphics, which finds its shaders by name. Nothing references
/// them, so player builds strip them and every pet falls back to a blob. Adds them to Always Included Shaders before each build.
/// </summary>
public sealed class IncludeVectorShaders : IPreprocessBuildWithReport
{
    static readonly string[] Names = { "Unlit/Vector", "Unlit/VectorGradient", "Hidden/VectorBlendMax", "Hidden/VectorDemultiply", "Hidden/VectorExpandEdges" };

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Ensure();

    [MenuItem("BookBuddies/Include Vector Shaders In Build")]
    public static void Ensure()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset"));
        var list = settings.FindProperty("m_AlwaysIncludedShaders");
        var have = new HashSet<Object>();
        for (int i = 0; i < list.arraySize; i++) have.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
        foreach (var name in Names)
        {
            var shader = Shader.Find(name);
            if (shader == null) { Debug.LogWarning("BookBuddies: shader not found: " + name); continue; }
            if (have.Contains(shader)) continue;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        }
        if (settings.ApplyModifiedProperties()) AssetDatabase.SaveAssets();
    }
}
