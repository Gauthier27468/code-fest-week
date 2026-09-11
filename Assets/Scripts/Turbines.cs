using UnityEngine;

/// <summary>Rotation continue d'un élément de décor (pales d'éolienne).</summary>
public class Turbines : MonoBehaviour
{
    [Tooltip("Vitesse de rotation, en degrés par seconde.")]
    public float rotationSpeed = 100f;

    [Tooltip("Axe de rotation local.")]
    public Vector3 rotateVector = new Vector3(0f, 0f, 1f);

    private void Update()
    {
        transform.Rotate(rotateVector * (rotationSpeed * Time.deltaTime));
    }
}
