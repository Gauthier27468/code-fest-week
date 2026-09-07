using UnityEngine;

public class MoveBird : MonoBehaviour
{

    public Vector3 moveMatrix;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.Translate(moveMatrix * Time.deltaTime);
    }
}
