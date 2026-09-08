using System.Collections.Generic;
using UnityEngine;

namespace MusicMod
{
    /// <summary>
    /// Stops clicks aimed at the panel from also reaching the game underneath.
    ///
    /// Besiege's buttons and popups are colliders answering Unity's legacy
    /// <c>OnMouseOver</c>, which is raycast from the cameras and knows nothing about
    /// uGUI's EventSystem. A canvas drawn over one hides it without stopping it, and
    /// raising the canvas does not help -- there is nothing to raise above.
    ///
    /// The lever is <c>Camera.eventMask</c>: a legacy mouse raycast uses
    /// <c>cullingMask &amp; eventMask</c>, so zeroing the event mask makes the game
    /// deaf to the mouse. This holds every camera's mask down while the pointer is
    /// inside the panel and puts each one back the moment it leaves.
    ///
    /// Three things it has to get right: gather the cameras every frame, since one
    /// built while the shield is up would otherwise be the hole in it; release from
    /// OnDisable as well -- a shield left up is a game whose own buttons have
    /// stopped answering; and stand down with the canvas, not just with the window.
    /// </summary>
    public class ClickShield : MonoBehaviour
    {
        private readonly List<Camera> held = new List<Camera>();
        private readonly List<int> masks = new List<int>();
        private RectTransform guarded;
        private bool up;

        /// <summary>The canvas the guarded rect is drawn on. Tab switches that off
        /// and leaves the window under it active, so `activeInHierarchy` alone would
        /// hold the shield up over a panel nobody can see -- a game gone deaf to the
        /// mouse for no visible reason.</summary>
        private Canvas drawnOn;

        /// <summary>The rect the pointer has to be inside for the shield to go up.</summary>
        public void Guard(RectTransform rect)
        {
            guarded = rect;
        }

        private void LateUpdate()
        {
            bool wanted = guarded != null
                       && guarded.gameObject.activeInHierarchy
                       && Drawn()
                       && RectTransformUtility.RectangleContainsScreenPoint(
                              guarded, Input.mousePosition, null);
            if (wanted)
            {
                Raise();
            }
            else
            {
                Lower();
            }
        }

        private void OnDisable()
        {
            Lower();
        }

        private void OnDestroy()
        {
            Lower();
        }

        /// <summary>Whether the panel is being drawn at all. True where there is no
        /// canvas to ask, which is what this did before there was one to ask.</summary>
        private bool Drawn()
        {
            if (drawnOn == null)
            {
                drawnOn = guarded.GetComponentInParent<Canvas>();
            }
            return drawnOn == null || drawnOn.enabled;
        }

        private void Raise()
        {
            // Rebuilt every frame rather than cached: a camera created while the
            // shield is up would otherwise never be covered.
            Lower();
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null)
                {
                    continue;
                }
                held.Add(camera);
                masks.Add(camera.eventMask);
                camera.eventMask = 0;
            }
            up = true;
        }

        private void Lower()
        {
            if (!up && held.Count == 0)
            {
                return;
            }
            for (int i = 0; i < held.Count; i++)
            {
                if (held[i] != null)
                {
                    held[i].eventMask = masks[i];
                }
            }
            held.Clear();
            masks.Clear();
            up = false;
        }
    }
}
