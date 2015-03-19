using UnityEngine;
using System.Collections;

// Camera draws the scene (that's jsut a cube) into a rendertexture
// It attempts to blit the rendertexture to the screen in OnRenderImage, 
// but that doesn't work.

public class Exp1 : MonoBehaviour {
    public RenderTexture mainRT;
    public RenderTexture secRT;
    public Mesh cube;


	// Use this for initialization
	void Start () {
        mainRT = new RenderTexture(Screen.width, Screen.height, 0);
        mainRT.Create();
        GetComponent<Camera>().targetTexture = mainRT;

        secRT = new RenderTexture(Screen.width, Screen.height, 0);
        secRT.Create();
	}

    void OnPreRender()
    {
        // clear RT with a nice red color
        RenderTexture.active = mainRT;
        GL.Clear(true, true, Color.blue);

        GetComponent<Camera>().targetTexture = mainRT;
    }


    void OnPostRender()
    {

    }


    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        GetComponent<Camera>().targetTexture = null;

        if (src == mainRT)
            Debug.Log("src is rt");

        if (dest == mainRT)
            Debug.Log("dest is rt"); // dest seems to be current target texture (or active RT)


        Graphics.Blit(mainRT, null as RenderTexture); // why u no work? :/
    }


	// Update is called once per frame
	void Update () {
	
	}


}
