using UnityEngine;

public class TileSnap : MonoBehaviour
{
    public float tileSize = 10f;

    void Update()
    {
        if (!Application.isPlaying)
        {
            SnapToGrid();
        }
    }

    void SnapToGrid()
    {
        Vector3 pos = transform.position;

        pos.x = Mathf.Round(pos.x / tileSize) * tileSize;
        pos.z = Mathf.Round(pos.z / tileSize) * tileSize;

        pos.y = 0f;

        transform.position = pos;
    }
}