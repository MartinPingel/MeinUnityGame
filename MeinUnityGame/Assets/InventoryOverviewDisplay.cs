using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Village.Storage
{
    /// <summary>Read-only F6 view of existing warehouses and the existing wagon cargo.</summary>
    public sealed class InventoryOverviewDisplay : MonoBehaviour
    {
        [Serializable]
        private sealed class Location
        {
            public string label;
            public BuildingWarehouse warehouse;
            public string[] goodsTypes;
        }

        [SerializeField] private Location[] locations = new Location[0];
        [SerializeField] private WagonDeliveryJob wagon;
        [SerializeField] private bool visible;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private Vector2 scroll;
        private GUIStyle textStyle;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
                visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible) return;
            if (textStyle == null)
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                    fontSize = 18,
                    richText = false,
                    wordWrap = true,
                    normal = { textColor = Color.white }
                };

            float scale = Mathf.Min(1f, Mathf.Min(Screen.width / 420f, Screen.height / 680f));
            if (scale <= 0f) return;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            GUI.depth = -110;
            var panel = new Rect((Screen.width / scale - 400f) * 0.5f,
                (Screen.height / scale - 640f) * 0.5f, 400f, 640f);
            GUI.color = new Color(0.04f, 0.04f, 0.04f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(panel.x + 12f, panel.y + 12f, panel.width - 24f, panel.height - 24f));
            GUILayout.Label("Bestandsübersicht – F6 ein/aus", textStyle);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (Location location in locations)
            {
                if (location == null) continue;
                GUILayout.Label(location.label, textStyle);
                if (location.warehouse == null)
                    GUILayout.Label("Lager nicht zugeordnet", textStyle);
                else if (location.goodsTypes != null)
                    foreach (string goodsType in location.goodsTypes)
                        if (!string.IsNullOrWhiteSpace(goodsType))
                            GUILayout.Label(GoodsLabel(goodsType) + ": " +
                                location.warehouse.GetQuantity(goodsType), textStyle);
                GUILayout.Space(10f);
            }

            GUILayout.Label("Michael / Fuhrmann", textStyle);
            NpcAgent carrier = wagon != null ? wagon.GetComponent<NpcAgent>() : null;
            if (wagon == null || carrier == null || carrier.Simulation == null)
                GUILayout.Label("Fracht nicht verfügbar – NPC-Anbindung prüfen", textStyle);
            else
            {
                int quantity = wagon.WagonLoad;
                GUILayout.Label("Aktuelle Fracht: " + (quantity > 0 ? GoodsLabel(wagon.CargoName) : "Leer"), textStyle);
                GUILayout.Label("Menge / Kapazität: " + quantity + " / " + wagon.LoadCapacity, textStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private static string GoodsLabel(string goodsType) =>
            goodsType == "Lebensmittel" ? "Nahrung" :
            goodsType == "Getränke" ? "Wasser (Getränke)" : goodsType;
#endif
    }
}
