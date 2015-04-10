//
// Permission is granted to use, copy, distribute and prepare derivative works of this
// software for any purpose and without fee, provided, that the above copyright notice
// and this statement appear in all copies.  Intel makes no representations about the
// suitability of this software for any purpose.  THIS SOFTWARE IS PROVIDED "AS IS."
// INTEL SPECIFICALLY DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, AND ALL LIABILITY,
// INCLUDING CONSEQUENTIAL AND OTHER INDIRECT DAMAGES, FOR THE USE OF THIS SOFTWARE,
// INCLUDING LIABILITY FOR INFRINGEMENT OF ANY PROPRIETARY RIGHTS, AND INCLUDING THE
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE.  Intel does not
// assume any responsibility for any errors which may appear in this software nor any
// responsibility to update it.
//--------------------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// A simple script that updates the text field of a UI.Text element
// Based on Aras's JS script in http://wiki.unity3d.com/index.php?title=FramesPerSecond
// His approach accumlates fps and averages it over an interval (rather than just track number of net frames by time taken, which isn't accurate)
[RequireComponent(typeof(Text))]
public class Fps : MonoBehaviour
{
    public const float updateInterval = 0.5f; // secs    
    private float accumFps;
    private int framesDrawn;
    private float timeLeft;
    private Text t;

    // Use this for initialization
    void Start()
    {
        framesDrawn = 0;
        accumFps = 0;
        timeLeft = updateInterval;
        t = this.GetComponent<Text>();
    }

    // Update is called once per frame
    void Update()
    {
        timeLeft -= Time.deltaTime;
        accumFps += Time.timeScale / Time.deltaTime;
        framesDrawn++;

        if (timeLeft <= 0.0f)
        {
            // calculate fps          
            float fps = accumFps / (float) framesDrawn;

            t.text = "FPS: " + fps.ToString("f2"); // display two decimal digits

            if (fps < 10)
                t.color = Color.red;
            else if (fps < 25)                
                t.color = Color.yellow;
            else
                t.color = Color.green;

            timeLeft = updateInterval;
            framesDrawn = 0;
            accumFps = 0.0f;
        }
    }
}
