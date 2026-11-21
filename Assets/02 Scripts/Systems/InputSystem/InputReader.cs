using UnityEngine;
using UnityEngine.InputSystem;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
///  Tool for utilizing the event system with the input actions
/// </summary>
[CreateAssetMenu(menuName = "InputReader")]
public class InputReader : ScriptableObject, InputSystem.IPlayerActions, InputSystem.IUIActions
{
    private InputSystem _gameInput;
    
    ////////////////////////////////////////////////////////////////////////// Set Up
    /// House Keeping to make sure it starts up and ends correctly.
    /// Make sure in scripts if you subscribe to an event to unsubscibe on Disable/Destroy
    
    private void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        // Only initialize immediately if we are actually in Play Mode (avoids running in Edit Mode)
        if (EditorApplication.isPlaying)
        {
            InitializeInput();
        }
#else
        InitializeInput();
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        CleanupInput();
    }

    private void InitializeInput()
    {
        if (_gameInput == null)
        {
            _gameInput = new InputSystem();

            _gameInput.Player.SetCallbacks(this);
            _gameInput.UI.SetCallbacks(this);

            SetPlayer();
        }
    }

    private void CleanupInput()
    {
        if (_gameInput != null)
        {
            // Remove callbacks before disabling/disposing
            _gameInput.Player.RemoveCallbacks(this);
            _gameInput.UI.RemoveCallbacks(this);

            _gameInput.Player.Disable();
            _gameInput.UI.Disable();
            _gameInput.Disable();
            
            // Dispose the generated C# wrapper and null it out so it rebuilds cleanly next session
            _gameInput.Dispose();
            _gameInput = null;
        }
    }

#if UNITY_EDITOR
    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            InitializeInput();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            CleanupInput();
        }
    }
#endif

    ////////////////////////////////////////////////////////////////////////// Switch Input Functions
    /// Changes which Events will react to inputs.
    
    public void SetPlayer() // GamePlay
    {
        _gameInput.Player.Enable();
        _gameInput.UI.Disable();
    }
    public void SetUI() // UI
    {
        _gameInput.Player.Disable();
        _gameInput.UI.Enable();
    }

    ////////////////////////////////////////////////////////////////////////// Events
    public event Action<Vector2> MoveEvent; 
    public event Action JumpEvent;
    public event Action JumpCancelledEvent;
    public event Action PauseEvent;
    public event Action ResumeEvent;
    public event Action SprintEvent;
    public event Action SprintCancelledEvent;
    public event Action CrouchEvent;
    public event Action CrouchCancelledEvent;
    public event Action AttackEvent;
    public event Action AttackCancelledEvent;
    public event Action BlockEvent;
    public event Action BlockCancelledEvent;
    public event Action InteractEvent;
    public event Action InteractCancelledEvent;

    ////// Player Events ///////////////
    /// 
    public void OnMove(InputAction.CallbackContext context) // Vector 2 
    {
        //Debug.Log(message: $"Phase:{context.phase},Value:{context.ReadValue<Vector2>()}");
        MoveEvent?.Invoke(context.ReadValue<Vector2>());
    }

    public void OnJump(InputAction.CallbackContext context) // Check if down then when up
    {
       if (context.phase == InputActionPhase.Performed)
        {
            JumpEvent?.Invoke();
        }
        if (context.phase == InputActionPhase.Canceled)
        {
            JumpCancelledEvent?.Invoke();
        }
    }

    public void OnSprint(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Performed)
        {
            SprintEvent?.Invoke();
        }
        if (context.phase == InputActionPhase.Canceled)
        {
            SprintCancelledEvent?.Invoke();
        }
    }

    public void OnCrouch(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Performed)
        {
            CrouchEvent?.Invoke();
        }
        if (context.phase == InputActionPhase.Canceled)
        {
            CrouchCancelledEvent?.Invoke();
        }
    }

    public void OnMainHand(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Performed)
        {
            AttackEvent?.Invoke();
        }
        if (context.phase == InputActionPhase.Canceled)
        {
            AttackCancelledEvent?.Invoke();
        }
    }

    public void OnOffHand(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Performed)
        {
            BlockEvent?.Invoke();
        }
        if (context.phase == InputActionPhase.Canceled)
        {
            BlockCancelledEvent?.Invoke();
        }
    }

    public void OnInteract(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Performed)
        {
            InteractEvent?.Invoke();
        }
        if (context.phase == InputActionPhase.Canceled)
        {
            InteractCancelledEvent?.Invoke();
        }
    }


    public void OnPause(InputAction.CallbackContext context) // PAUSE
    {
        if (context.phase == InputActionPhase.Performed)
        {
            PauseEvent.Invoke();
            SetUI();
        }
    }

    ////// UI Events ///////////////////
    /// 
    public void OnResume(InputAction.CallbackContext context) // RESUME
    {
        if (context.phase == InputActionPhase.Performed)
        {
            ResumeEvent.Invoke();
            SetPlayer();
        }
    }


    ///////////////////////////////////////////////////////////////////////// Functions 
    
    ////// Implemented /////////////////
    ///
    



    ////// Not Implemented /////////////
    /// Interfaces will automaticaly be generated 
    
    public void OnLook(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }


    public void OnPrevious(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnNext(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnNavigate(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnSubmit(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnCancel(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnPoint(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnClick(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnRightClick(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnMiddleClick(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnScrollWheel(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnTrackedDevicePosition(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }

    public void OnTrackedDeviceOrientation(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        //throw new System.NotImplementedException();
    }
}

    ////// Examples ////////////////////
    /// For refrence on how to write the diffrent call backs and events
    /*
        // Events

        public event Action<Vector2> MoveEvent; 
        public event Action JumpEvent;
        public event Action JumpCancelledEvent;
        public event Action PauseEvent;
        public event Action ResumeEvent;

        // Functions

        public void OnMove(InputAction.CallbackContext context) // Vector 2 
        {
            //Debug.Log(message: $"Phase:{context.phase},Value:{context.ReadValue<Vector2>()}");
            MoveEvent?.Invoke(context.ReadValue<Vector2>());
        }

        public void OnJump(InputAction.CallbackContext context) // Check if down then when up
        {
            if (context.phase == InputActionPhase.Performed)
            {
                JumpEvent?.Invoke();
            }
            if (context.phase == InputActionPhase.Canceled)
            {
                JumpCancelledEvent?.Invoke();
            }
        }
        public void OnPause(InputAction.CallbackContext context) // PAUSE
        {
            if (context.phase == InputActionPhase.Performed)
            {
                PauseEvent.Invoke();
                SetUI();
            }
        }

        public void OnResume(InputAction.CallbackContext context) // RESUME
        {
            if (context.phase == InputActionPhase.Performed)
            {
                ResumeEvent.Invoke();
                SetPlayer();
            }
        }
    */
