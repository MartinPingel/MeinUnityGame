using UnityEngine;
using UnityEngine.InputSystem;

namespace Village.Storage
{
    /// <summary>Read-only development overlay. No dependency on NPC observation or global UI.</summary>
    public sealed class WarehouseDebugDisplay : MonoBehaviour
    {
        [SerializeField] private BuildingWarehouse warehouse;
        [SerializeField] private bool visible = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private Vector2 scroll;
        private GUIStyle textStyle;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
                visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || warehouse == null) return;
            if (textStyle == null)
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                    fontSize = 18,
                    richText = false,
                    wordWrap = true,
                    normal = { textColor = Color.white }
                };

            // Fit only this overlay, without changing the scene canvas or camera.
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            float scale = Mathf.Min(1f, Mathf.Min(Screen.width / 360f, Screen.height / 360f));
            if (scale <= 0f) return;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            GUI.depth = -100;
            var panel = new Rect(Screen.width / scale - 332f, 70f, 320f, 270f);
            GUI.color = new Color(0.04f, 0.04f, 0.04f, 0.88f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(panel.x + 12f, panel.y + 10f, panel.width - 24f, panel.height - 20f));
            GUILayout.Label("Warenlager – " + warehouse.WarehouseName, textStyle);
            GUILayout.Label("F5: Anzeige ein/aus", textStyle);
            scroll = GUILayout.BeginScrollView(scroll);
            StockRecord[] entries = warehouse.GetSnapshot();
            if (entries.Length == 0) GUILayout.Label("Leer – keine Waren vorhanden.", textStyle);
            foreach (StockRecord entry in entries)
                GUILayout.Label(entry.GoodsType + ": " + entry.Quantity, textStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
            GUI.depth = previousDepth;
        }
#endif
    }
}
