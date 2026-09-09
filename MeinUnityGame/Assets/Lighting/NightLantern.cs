using UnityEngine;

/// <summary>Reusable lantern prefab. Place under a VillageLighting group.</summary>
[DisallowMultipleComponent]
public sealed class NightLantern : MonoBehaviour
{
    [SerializeField] private Light warmLight;
    [SerializeField] private GameObject flameGlow;
    private VillageLighting group;

    private void OnEnable() => BindGroup();

    private void OnTransformParentChanged()
    {
        if (isActiveAndEnabled) BindGroup();
    }

    private void BindGroup()
    {
        if (group != null) group.Unregister(this);
        group = GetComponentInParent<VillageLighting>();
        if (group != null) group.Register(this);
        else SetLit(false);
    }

    private void OnDisable()
    {
        if (group != null) group.Unregister(this);
        group = null;
        SetLit(false);
    }

    public void SetLit(bool lit)
    {
        if (warmLight != null && warmLight.enabled != lit) warmLight.enabled = lit;
        if (flameGlow != null && flameGlow.activeSelf != lit) flameGlow.SetActive(lit);
    }
}
