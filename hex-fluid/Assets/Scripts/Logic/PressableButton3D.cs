#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System;
using UnityEngine;
using UnityEngine.Events;

namespace HexFluid
{
    /// <summary>
    /// A 3D push-button, like a cube you press. Hovering dips it down a little on Y;
    /// pressing pushes it further and it springs back on release (classic click feel).
    /// On a completed click it logs a message and fires onClick / Clicked.
    ///
    /// Setup:
    ///   - Put this on a Cube (GameObject > 3D Object > Cube) — it already has a
    ///     BoxCollider, which the raycast needs.
    ///   - Press Play, move the mouse over it, click.
    ///   - Hook your "start the game" logic to onClick (inspector) or Clicked (code).
    ///
    /// It moves local Y, so parent/rotate the cube however you like.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PressableButton3D : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Camera used for picking. Empty = Camera.main.")]
        public Camera cam;

        [Header("Motion (local Y, world units)")]
        [Tooltip("How far it dips while hovered.")]
        public float hoverDepth = 0.05f;

        [Tooltip("How far it pushes down while held.")]
        public float pressDepth = 0.15f;

        [Tooltip("Higher = snappier. ~10-18 feels good.")]
        public float speed = 14f;

        [Header("Audio")]
        [Tooltip("Played the moment the button is pressed down.")]
        public SoundSO pressSound;

        [Header("On click")]
        [Tooltip("Printed to the Console on each click. Leave empty to print nothing.")]
        public string debugMessage = "Game started";

        [Tooltip("Hook your start-the-game logic here in the inspector.")]
        public UnityEvent onClick = new UnityEvent();

        [Tooltip("Keep working while a popup blocks the game's input (e.g. the main menu's own buttons).")]
        public bool ignoreInputBlocker = false;

        /// <summary>Fires on a completed click (press + release over the button).</summary>
        public event Action Clicked;

        /// <summary>True while the mouse is over the button this frame.</summary>
        public bool IsHovered { get; private set; }

        /// <summary>True while the button is held down.</summary>
        public bool IsPressed { get; private set; }

        private float _baseY;
        private bool _pressedOnThis;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
            _baseY = transform.localPosition.y;

            if (GetComponent<Collider>() == null)
                Debug.LogWarning("PressableButton3D needs a Collider to be clickable (a Cube has one).", this);
        }

        private void Update()
        {
            if (cam == null) { cam = Camera.main; if (cam == null) return; }

            IsHovered = (ignoreInputBlocker || !InputBlocker.Blocked) && RaycastHitsSelf();   // a popup is open: can't be clicked through it

            // Press / release logic (classic button: down on press, click fires on release over it).
            if (LeftPressed() && IsHovered)
            {
                _pressedOnThis = true;
                IsPressed = true;
                Sfx.Play(pressSound);
            }

            if (LeftReleased())
            {
                if (_pressedOnThis && IsHovered) DoClick();
                _pressedOnThis = false;
                IsPressed = false;
            }

            // Target height for this frame.
            float target = _baseY;
            if (IsPressed) target -= pressDepth;
            else if (IsHovered) target -= hoverDepth;

            // Frame-rate independent ease toward the target.
            float k = 1f - Mathf.Exp(-speed * Time.deltaTime);
            Vector3 p = transform.localPosition;
            p.y = Mathf.Lerp(p.y, target, k);
            transform.localPosition = p;
        }

        private void DoClick()
        {
            if (!string.IsNullOrEmpty(debugMessage)) Debug.Log(debugMessage, this);
            onClick?.Invoke();
            Clicked?.Invoke();
        }

        private bool RaycastHitsSelf()
        {
            Ray ray = cam.ScreenPointToRay(MousePosition());
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
                return hit.collider != null &&
                       (hit.transform == transform || hit.transform.IsChildOf(transform));
            return false;
        }

        // ---- Input (old Input Manager or new Input System) --------------------

        private static Vector3 MousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return Vector3.zero;
            Vector2 p = Mouse.current.position.ReadValue();
            return new Vector3(p.x, p.y, 0f);
#else
            return Input.mousePosition;
#endif
        }

        private static bool LeftPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool LeftReleased()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(0);
#endif
        }
    }
}