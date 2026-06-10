using UnityEditor;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  /// <summary>
  /// Small modal result dialog. EditorUtility.DisplayDialog renders a fixed
  /// platform icon (a warning triangle on Linux), so a success result cannot be
  /// shown with a checkmark there. This draws the icon explicitly — a green
  /// checkmark on success, the console error icon on failure.
  /// </summary>
  class SteamDeckResultDialog : EditorWindow
  {
    string m_Message;
    Texture m_Icon;

    public static void Show(string title, string message, bool success)
    {
      var window = CreateInstance<SteamDeckResultDialog>();
      window.titleContent = new GUIContent(title);
      window.m_Message = message;
      window.m_Icon = EditorGUIUtility
        .IconContent(success ? "GreenCheckmark" : "console.erroricon")
        ?.image;
      window.minSize = window.maxSize = new Vector2(360, 120);
      window.ShowModalUtility();
    }

    void OnGUI()
    {
      EditorGUILayout.Space(12);

      using (new EditorGUILayout.HorizontalScope())
      {
        GUILayout.Space(12);
        if (m_Icon != null)
          GUILayout.Label(m_Icon, GUILayout.Width(40), GUILayout.Height(40));
        GUILayout.Space(8);
        GUILayout.Label(m_Message, EditorStyles.wordWrappedLabel, GUILayout.Height(40));
      }

      GUILayout.FlexibleSpace();

      using (new EditorGUILayout.HorizontalScope())
      {
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("OK", GUILayout.Width(96), GUILayout.Height(24)))
          Close();
        GUILayout.Space(12);
      }

      EditorGUILayout.Space(12);
    }
  }
}
