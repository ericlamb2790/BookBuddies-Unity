using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F11 (or Window > General > Game Fullscreen) opens a borderless Game view covering the main monitor,
/// with no toolbar. Press F11 again, or leave Play mode, to close it.
/// </summary>
public static class FullscreenGameView
{
    static readonly Type GameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
    static EditorWindow instance;

    [MenuItem("Window/General/Game Fullscreen _F11", priority = 2)]
    public static void Toggle()
    {
        if (instance != null) { Close(); return; }
        if (GameViewType == null) { Debug.LogError("FullscreenGameView: couldn't find Unity's GameView type."); return; }

        instance = (EditorWindow)ScriptableObject.CreateInstance(GameViewType);
        GameViewType.GetProperty("showToolbar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.SetValue(instance, false);

        var res = Screen.currentResolution;
        float ppp = EditorGUIUtility.pixelsPerPoint;
        instance.ShowPopup();
        instance.position = new Rect(0, 0, res.width / ppp, res.height / ppp);
        instance.Focus();
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void OnPlayMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.ExitingPlayMode) Close();
    }

    static void Close()
    {
        EditorApplication.playModeStateChanged -= OnPlayMode;
        if (instance != null) instance.Close();
        instance = null;
    }
}
