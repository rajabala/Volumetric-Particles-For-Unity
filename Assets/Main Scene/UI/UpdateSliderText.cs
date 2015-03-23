using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(Slider))]
public class UpdateSliderText : MonoBehaviour {
    public Text textValue;

    void Start()
    {
        UpdateText(this.GetComponent<Slider>().value);        
    }

	public void UpdateText(float value)
    {
        textValue.text = "(" + value.ToString() + ")";
    }
}
