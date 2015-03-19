using UnityEngine;
using System.Collections;

// Ideally, this'd be a static class, but to hook it to the new UI event system, we gotta be able to attach this script to a game object.
public class GUIOptions : MonoBehaviour {
    public bool bShowMetavoxelGrid, 
                bShowMetavoxelDrawOrder, 
                bShowInstructions, 
                bShowRayMarchSamplesPerPixel
                ;

    public float fDisplacementScale;

    public void ShowOrHideAllGUIElements()
    {

    }

    public void ToggleShowMetavoxelGrid()
    {
        bShowMetavoxelGrid = !bShowMetavoxelGrid;
    }

    public void ToggleShowMetavoxelDrawOrder()
    {
        bShowMetavoxelDrawOrder = !bShowMetavoxelDrawOrder;
    }

    public void SetDisplacementScale(float ds)
    {
        fDisplacementScale = ds;
    }

    public void ShowInstructions()
    {

    }

}
