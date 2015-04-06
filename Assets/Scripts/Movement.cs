using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// Attach this script to the main camera to move around
public class Movement : MonoBehaviour {
    public Light dirLight;
    public float lookSpeed, moveSpeed;
    public Text instructions, beingMovedText;

    private bool moveCamera, moveLight;
    private float camRotationX, camRotationY, lightRotationX, lightRotationY;
    private Vector3 startPos; private Quaternion startRot;

	// Use this for initialization
	void Start () {     
        startPos = transform.position;
        startRot = transform.rotation;
	}
	
	// Update is called once per frame
	void Update () {
        ProcessInput();
	}


    void ProcessInput()
    {
        if (moveCamera)
        {
            // fps'ish free cam
            camRotationX += Input.GetAxis("Mouse X") * lookSpeed;
            camRotationY += Input.GetAxis("Mouse Y") * lookSpeed;
            camRotationY = Mathf.Clamp(camRotationY, -90, 90); // can't do a backflip of course.

            // Unity's screen space coordinate convention has the origin on the bottom left
            // Using the camera's up and right vector can lose one degree of freedom and cause the gimbal lock!
            transform.localRotation = Quaternion.AngleAxis(camRotationX, Vector3.up);
            transform.localRotation *= Quaternion.AngleAxis(camRotationY, -Vector3.right);

            transform.position += transform.forward * Input.GetAxis("Vertical") * moveSpeed;
            transform.position += transform.right * Input.GetAxis("Horizontal") * moveSpeed;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    // move in -X in local camera space
                    transform.position -= moveSpeed * transform.up;
                }
                else
                    transform.position += moveSpeed * transform.up;
            }
        }
        else if (moveLight)
        {
            // fps'ish free cam
            lightRotationX += Input.GetAxis("Mouse X") * lookSpeed;
            lightRotationY += Input.GetAxis("Mouse Y") * lookSpeed;

            // Unity's screen space coordinate convention has the origin on the bottom left
            // Using the camera's up and right vector can lose one degree of freedom and cause the gimbal lock!
            dirLight.transform.localRotation = Quaternion.AngleAxis(lightRotationX, Vector3.up);
            dirLight.transform.localRotation *= Quaternion.AngleAxis(lightRotationY, -Vector3.right);
        }


        if (Input.GetKeyDown(KeyCode.R))
        {
            transform.position = startPos;
            transform.rotation = startRot;
            camRotationX = camRotationY = 0.0f;
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            moveCamera = !moveCamera;
            if (moveCamera)
            {
                moveLight = false;
                beingMovedText.text = "Moving camera..";
            }
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            moveLight = !moveLight;
            if (moveLight)
            {
                moveCamera = false;
                beingMovedText.text = "Moving light..";
            }
        }

        if (!moveCamera && !moveLight)
            beingMovedText.text = "";

        if (Input.GetKeyDown(KeyCode.H))
        {
            instructions.gameObject.SetActive(!instructions.gameObject.activeSelf);
        }


    }
}
