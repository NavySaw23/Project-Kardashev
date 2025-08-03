using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class manager : MonoBehaviour
{
    public bool slowmode = false;

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (slowmode)
        {
            Time.timeScale = 0.1f; // Slow down time
        }
        else
        {
            Time.timeScale = 1f; // Normal speed
        }
        
    }
}
