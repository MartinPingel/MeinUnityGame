using UnityEngine;
using UnityEngine.InputSystem;
using System.Globalization;

namespace Village.Storage
{
    /// <summary>Read-only development overlay. No dependency on NPC observation or global UI.</summary>
    public sealed class WarehouseDebugDisplay : MonoBehaviour
    {
        [SerializeField] private BuildingWarehouse warehouse;
        [SerializeField] private BuildingWarehouse[] additionalWarehouses = new BuildingWarehouse[0];
        [SerializeField] private BuildingWarehouse[] warehousesWithDrinks = new BuildingWarehouse[0];
        [System.Serializable]
        private sealed class StockWatch
        {
            public BuildingWarehouse warehouse;
            public string[] goodsTypes;
        }
        [SerializeField] private StockWatch[] trackedWarehouses = new StockWatch[0];
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
            float scale = Mathf.Min(1f, Mathf.Min(Screen.width / 360f, Screen.height / 450f));
            if (scale <= 0f) return;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            GUI.depth = -100;
            var panel = new Rect(Screen.width / scale - 332f, 70f, 320f, 360f);
            GUI.color = new Color(0.04f, 0.04f, 0.04f, 0.88f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(panel.x + 12f, panel.y + 10f, panel.width - 24f, panel.height - 20f));
            GUILayout.Label("Werkzeugzustand", textStyle);
            GUILayout.Label("F5: Anzeige ein/aus", textStyle);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (StockWatch watch in trackedWarehouses)
                if (watch != null && watch.warehouse != null)
                    DrawWarehouse(watch.warehouse, watch.goodsTypes);
            DrawWarehouse(warehouse, new[] { "Lebensmittel", "Werkzeuge" });
            foreach (BuildingWarehouse additional in additionalWarehouses)
                if (additional != null && additional != warehouse) DrawWarehouse(additional);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
            GUI.depth = previousDepth;
        }
        private void DrawWarehouse(BuildingWarehouse target, string[] trackedGoods = null)
        {
            WorkplaceTools tools = target.Tools;
            if (tools == null) return;
            GUILayout.Label(target.WarehouseName, textStyle);
            if (tools != null)
            {
                GUILayout.Label("Haltbarkeit: " + tools.CurrentDurability.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) +
                    " / " + tools.MaximumDurability.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")), textStyle);
                GUILayout.Label(tools.HasUsableTool ? "Werkzeug verfügbar" :
                    (!tools.HasEverHadTool ? "Einmalige Startphase ohne Werkzeug" : "Produktion gesperrt: Werkzeug fehlt"), textStyle);
            }
            GUILayout.Space(8f);
        }
#endif
    }
}
