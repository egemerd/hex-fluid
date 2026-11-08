#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HexFluid.EditorTools
{
    /// <summary>
    /// LevelManager's Inspector, plus a read-only "Play order" list under it that
    /// marks the boss levels, so you can see where the bosses fall in the run while
    /// ordering the levels. In Play mode the level being played is marked with ▶.
    /// </summary>
    [CustomEditor(typeof(LevelManager))]
    public class LevelManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (LevelManager)target;
            if (manager.levels == null || manager.levels.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Play order", EditorStyles.boldLabel);

            var bossStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
            bossStyle.normal.textColor = new Color(0.9f, 0.35f, 0.3f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < manager.levels.Count; i++)
                {
                    LevelData level = manager.levels[i];
                    bool current = Application.isPlaying && i == manager.CurrentIndex;
                    string prefix = $"{(current ? "▶" : "  ")} {i + 1}.";

                    if (level == null)
                    {
                        EditorGUILayout.LabelField($"{prefix} (empty)");
                        continue;
                    }

                    if (!level.isBoss)
                    {
                        EditorGUILayout.LabelField($"{prefix} {level.name}");
                        continue;
                    }

                    string boss = level.boss != null ? level.boss.DisplayName : "no boss assigned!";
                    EditorGUILayout.LabelField($"{prefix} {level.name}   ★ BOSS: {boss}", bossStyle);
                }
            }
        }
    }
}
#endif
