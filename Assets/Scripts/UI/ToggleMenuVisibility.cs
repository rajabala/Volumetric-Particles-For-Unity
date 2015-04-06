using UnityEngine;
using System.Collections;

public class ToggleMenuVisibility : MonoBehaviour {
    public GameObject options;

    public void ToggleMenu()
    {
        if (options)
        {
            options.SetActive(!options.activeSelf);
        }
    }
}
