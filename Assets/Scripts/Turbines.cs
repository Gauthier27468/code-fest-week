using UnityEngine;

public class Turbines : MonoBehaviour
{
    public float rotationSpeed = 100f;
    public Vector3 rotateVector = new Vector3(0f, 0f, 1f);

    // Update is called once per frame
    void Update()
    {

        Vector3 rotation = rotateVector * rotationSpeed * Time.deltaTime;
        transform.Rotate(rotation);
    }
}
