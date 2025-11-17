using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem; // Required for the new Input System
using System.Collections.Generic;

public class Main : MonoBehaviour
{
    public PsarcLoader psarcLoader;
    public NoteHighway noteHighway;
    public Button playPauseButton;
    public Slider playbackScrubber;
    public Button muteButton;
    public Button newSongButton; // New slot for the "New Song" button
    public Slider videoOffsetSlider; // Slider for video offset
    public TextMeshProUGUI videoOffsetLabel; // Label to display the current video offset value
    public Slider songSpeedSlider; // Slider for song speed
    public TextMeshProUGUI songSpeedLabel; // Label to display the current song speed value
    public Button applySpeedButton; // Button to apply the pending speed change
    public TextMeshProUGUI loadingText; // Reference to the loading text UI element

    // Maximum size of the audio cache in megabytes (MB)
    public int audioCacheLimitMB = 1024;

    public bool runInBackground = true; // Option to keep the game running when focus is lost

    private bool isPlaying = false;
    private float _initialVideoOffsetMs = 0f; // Store the default value from NoteHighway.cs on startup
    private float _appliedSongSpeed = 100f; // Store the currently applied song speed
    private bool isUpdatingScrubberFromCode = false; // Flag to prevent feedback loop
    private bool isUpdatingSongSpeedFromCode = false; // Flag to prevent feedback loop for song speed slider
    private bool isUpdatingVideoOffsetFromCode = false; // Flag to prevent feedback loop for video offset slider

    private float _barNavigationTimer = 0f;
    private const float _barNavigationInitialDelay = 0.5f; // Delay before repeat starts
    private const float _barNavigationRepeatRate = 0.1f; // Time between repeats
    private int _barNavigationDirection = 0; // -1 for left, 1 for right, 0 for none

    public void SetLoadingText(bool isLoading)
    {
        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(isLoading);
        }
    }

    void Start()
    {
        // Set application behavior based on user option
        Application.runInBackground = runInBackground;

        playPauseButton.onClick.AddListener(TogglePlayPause);
        muteButton.onClick.AddListener(ToggleMute);
        playbackScrubber.onValueChanged.AddListener(OnScrubberValueChanged);
        newSongButton.onClick.AddListener(NewSong); // Attach the new method
        
        // Video Offset Slider setup
        if (videoOffsetSlider != null && noteHighway != null)
        {
            videoOffsetSlider.minValue = -200f;
            videoOffsetSlider.maxValue = 200f;
            videoOffsetSlider.onValueChanged.AddListener(OnVideoOffsetValueChanged);
            
            // Store the default value from NoteHighway and set initial slider value and label
            _initialVideoOffsetMs = noteHighway.videoOffsetMs;
            videoOffsetSlider.value = _initialVideoOffsetMs;
            UpdateVideoOffsetLabel(_initialVideoOffsetMs);
        }

        // Song Speed Slider setup
        if (songSpeedSlider != null)
        {
            songSpeedSlider.minValue = 50f;
            songSpeedSlider.maxValue = 150f;
            songSpeedSlider.wholeNumbers = true; // Ensure 1% steps by default
            _appliedSongSpeed = 100f; // Initialize applied speed
            songSpeedSlider.value = _appliedSongSpeed;
            songSpeedSlider.onValueChanged.AddListener(OnSongSpeedValueChanged);
            UpdateSongSpeedLabel(_appliedSongSpeed);
        }

        // Apply Speed Button setup
        if (applySpeedButton != null)
        {
            applySpeedButton.onClick.AddListener(ApplySongSpeedChange);
            applySpeedButton.gameObject.SetActive(false); // Hide initially
        }

        // Set initial mute button text based on PsarcLoader's setting
        if (psarcLoader != null)
        {
            UpdateMuteButtonText();
        }
    }

    void Update()
    {
        // Ensure a keyboard is present before checking for input
        if (Keyboard.current == null) return;

        // Mouse wheel scrolling using new Input System (no UnityEngine.Input in this project)
        if (Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            bool mouseInGameWindow = mousePos.x >= 0 && mousePos.x < Screen.width && mousePos.y >= 0 && mousePos.y < Screen.height;
            if (Application.isFocused && mouseInGameWindow)
            {
                float scrollDelta = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scrollDelta) > 0.1f)
                {
                    // Scroll by 1 second (1.0f) per scroll unit. Negative delta for forward scroll.
                    float timeJump = -scrollDelta * 1.0f;
                    JumpByTime(timeJump);
                }
            }
        }

        // Keyboard controls using the new Input System
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TogglePlayPause();
        }

        if (Keyboard.current.mKey.wasPressedThisFrame)
        {
            ToggleMute();
        }

        // Handle initial press for bar navigation
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            _barNavigationDirection = -1;
            _barNavigationTimer = Time.time + _barNavigationInitialDelay;
            NavigateByBar(-1);
        }
        else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            _barNavigationDirection = 1;
            _barNavigationTimer = Time.time + _barNavigationInitialDelay;
            NavigateByBar(1);
        }
        // Stop repeating if neither key is pressed
        else if (!Keyboard.current.leftArrowKey.isPressed && !Keyboard.current.rightArrowKey.isPressed)
        {
            _barNavigationDirection = 0;
        }

        // Handle repeat navigation if key is held
        if (_barNavigationDirection != 0 && Time.time >= _barNavigationTimer)
        {
            NavigateByBar(_barNavigationDirection);
            _barNavigationTimer = Time.time + _barNavigationRepeatRate;
        }

        if (psarcLoader?.SongDuration > 0 && psarcLoader.IsPlaying)
        {
            // Set flag to prevent OnScrubberValueChanged from firing the reset logic
            isUpdatingScrubberFromCode = true;
            playbackScrubber.value = psarcLoader.PlaybackTime / psarcLoader.SongDuration;
            isUpdatingScrubberFromCode = false;
        }
    }

    /// <summary>
    /// Updates the song speed UI elements (slider and label) to reflect a new applied speed.
    /// </summary>
    /// <param name="speed">The new song speed percentage to display and track.</param>
    public void UpdateSongSpeedUI(float speed)
    {
        if (songSpeedSlider != null)
        {
            // Clamp the speed to the slider's range just in case
            float clampedSpeed = Mathf.Clamp(speed, songSpeedSlider.minValue, songSpeedSlider.maxValue);

            isUpdatingSongSpeedFromCode = true;
            songSpeedSlider.value = clampedSpeed;
            isUpdatingSongSpeedFromCode = false;

            _appliedSongSpeed = clampedSpeed; // Update the applied speed tracker
            UpdateSongSpeedLabel(clampedSpeed);

            // Ensure the apply button is hidden after setting the speed
            if (applySpeedButton != null)
            {
                applySpeedButton.gameObject.SetActive(false);
            }
        }
    }

    public void NewSong()
    {
        if (psarcLoader != null)
        {
            // Reset state before starting the load process
            isPlaying = false;
            UpdatePlayPauseButtonText();

            psarcLoader.NewSong();
            
            // 1. Apply default video offset
            if (videoOffsetSlider != null && noteHighway != null)
            {
                videoOffsetSlider.value = _initialVideoOffsetMs;
                noteHighway.videoOffsetMs = _initialVideoOffsetMs;
                UpdateVideoOffsetLabel(_initialVideoOffsetMs); // Update label on song load
            }
            
            // The state update is now handled asynchronously in SongLoadedAndStarted()
            // which is called by PsarcLoader after the song is loaded and playback starts.
        }
    }

    /// <summary>
    /// Called by PsarcLoader after a song has been successfully loaded and playback has started.
    /// </summary>
    public void SongLoadedAndStarted()
    {
        if (psarcLoader == null) return;
        
        // Reset scrubber to 0 (start of song)
        isUpdatingScrubberFromCode = true;
        playbackScrubber.value = 0f;
        isUpdatingScrubberFromCode = false;
            
        // Force the state to 'playing' since we just initiated playback
        isPlaying = psarcLoader.IsPlaying;
        UpdatePlayPauseButtonText();
    }

    void TogglePlayPause()
    {
        if (psarcLoader != null)
        {
            psarcLoader.TogglePlayback();
            isPlaying = psarcLoader.IsPlaying;
            UpdatePlayPauseButtonText();
        }
    }

    void ToggleMute()
    {
        if (psarcLoader == null || psarcLoader.audioSource == null) return;

        psarcLoader.audioSource.mute = !psarcLoader.audioSource.mute;
        UpdateMuteButtonText();
    }

    void OnScrubberValueChanged(float value)
    {
        // If the code is updating the scrubber, ignore this event
        if (isUpdatingScrubberFromCode) return;

        if (psarcLoader?.SongDuration <= 0) return;

        float targetTime = value * psarcLoader.SongDuration;
        psarcLoader.JumpToTime(targetTime);
    }


    void UpdatePlayPauseButtonText()
    {
        var tmproLabel = playPauseButton.GetComponentInChildren<TextMeshProUGUI>();
        if (tmproLabel != null)
        {
            tmproLabel.text = isPlaying ? "Pause" : "Play";
        }
        else
        {
            var textLabel = playPauseButton.GetComponentInChildren<Text>();
            if (textLabel != null)
            {
                textLabel.text = isPlaying ? "Pause" : "Play";
            }
        }
    }

    void UpdateMuteButtonText()
    {
        bool isMuted = psarcLoader.audioSource.mute;
        var tmproLabel = muteButton.GetComponentInChildren<TextMeshProUGUI>();
        if (tmproLabel != null)
        {
            tmproLabel.text = isMuted ? "Unmute" : "Mute";
        }
        else
        {
            var textLabel = muteButton.GetComponentInChildren<Text>();
            if (textLabel != null)
            {
                textLabel.text = isMuted ? "Unmute" : "Mute";
            }
        }
    }

    void OnVideoOffsetValueChanged(float value)
    {
        if (isUpdatingVideoOffsetFromCode) return;

        float snappedValue = value;
        // Check if the Option/Alt key is NOT pressed (Keyboard.current.leftAltKey.isPressed)
        // If not pressed, snap to the nearest 5ms
        if (Keyboard.current != null && !Keyboard.current.leftAltKey.isPressed)
        {
            // Snap to nearest 5
            snappedValue = Mathf.Round(value / 5f) * 5f;
            
            // Clamp the snapped value to the slider's min/max range
            snappedValue = Mathf.Clamp(snappedValue, videoOffsetSlider.minValue, videoOffsetSlider.maxValue);

            // If the snapped value is different from the current slider value, update the slider
            if (Mathf.Abs(snappedValue - value) > 0.1f)
            {
                isUpdatingVideoOffsetFromCode = true;
                videoOffsetSlider.value = snappedValue;
                isUpdatingVideoOffsetFromCode = false;
                // Use the snapped value for the rest of the logic
                value = snappedValue;
            }
        }

        if (noteHighway != null)
        {
            noteHighway.videoOffsetMs = value;
        }
        UpdateVideoOffsetLabel(value);
    }

    void UpdateVideoOffsetLabel(float offsetMs)
    {
        if (videoOffsetLabel != null)
        {
            // DO NOT CHANGE THIS LABEL!
            videoOffsetLabel.text = $"{offsetMs:F0}ms";
        }
    }

    void OnSongSpeedValueChanged(float value)
    {
        if (isUpdatingSongSpeedFromCode) return;

        // Pause the song immediately upon adjustment
        if (psarcLoader?.audioSource?.isPlaying == true)
        {
            TogglePlayPause();
        }

        float snappedValue = value;
        // Check if the Option/Alt key is NOT pressed (Keyboard.current.leftAltKey.isPressed)
        // If not pressed, snap to the nearest 5%
        if (Keyboard.current != null && !Keyboard.current.leftAltKey.isPressed)
        {
            // Snap to nearest 5
            snappedValue = Mathf.Round(value / 5f) * 5f;
            
            // Clamp the snapped value to the slider's min/max range
            snappedValue = Mathf.Clamp(snappedValue, songSpeedSlider.minValue, songSpeedSlider.maxValue);

            // If the snapped value is different from the current slider value, update the slider
            if (Mathf.Abs(snappedValue - value) > 0.1f)
            {
                isUpdatingSongSpeedFromCode = true;
                songSpeedSlider.value = snappedValue;
                isUpdatingSongSpeedFromCode = false;
                // Use the snapped value for the rest of the logic
                value = snappedValue;
            }
        }

        UpdateSongSpeedLabel(value);
        
        // Show/hide the apply button if the slider value differs from the applied speed
        if (applySpeedButton != null)
        {
            // Using a small threshold for float comparison, comparing against the last applied speed
            // This is primarily for discrete controls (like a speed slider with integer steps)
            bool hasPendingChange = Mathf.Abs(value - _appliedSongSpeed) > 0.1f; 
            applySpeedButton.gameObject.SetActive(hasPendingChange);
        }
    }
    
    void UpdateSongSpeedLabel(float speed)
    {
        if (songSpeedLabel != null)
        {
            // DO NOT CHANGE THIS LABEL!
            songSpeedLabel.text = $"{speed:F0}%";
        }
    }

    void ApplySongSpeedChange()
    {
        if (psarcLoader != null)
        {
            float newSpeed = songSpeedSlider.value;
            float oldSpeed = _appliedSongSpeed; // Capture the current applied speed before updating
            
            psarcLoader.SetSongSpeed(newSpeed, oldSpeed);
            _appliedSongSpeed = newSpeed;

            // Save the last played speed to the arrangement data
            if (psarcLoader.ArrangementData != null)
            {
                psarcLoader.ArrangementData.LastPlayedSpeed = newSpeed;
                psarcLoader.SaveSongSettings(psarcLoader.ArrangementData);
            }
        }

        // Hide the button after applying the change
        if (applySpeedButton != null)
        {
            applySpeedButton.gameObject.SetActive(false);
        }
    }

    void NavigateByBar(int direction)
    {
        if (psarcLoader?.audioSource?.clip == null || psarcLoader.ArrangementData?.BarTimes == null) return;

        float currentTime = psarcLoader.audioSource.time;
        List<float> barTimes = psarcLoader.ArrangementData.BarTimes;

        // Find the current bar index
        int currentBarIndex = -1;
        for (int i = 0; i < barTimes.Count; i++)
        {
            if (currentTime < barTimes[i])
            {
                currentBarIndex = i - 1;
                break;
            }
        }
        // If we are past the last bar, set currentBarIndex to the last bar
        if (currentBarIndex == -1 && barTimes.Count > 0 && currentTime >= barTimes[barTimes.Count - 1])
        {
            currentBarIndex = barTimes.Count - 1;
        }
        // If we are before the first bar, set currentBarIndex to -1 (or 0 if moving forward)
        if (currentBarIndex == -1 && barTimes.Count > 0 && currentTime < barTimes[0])
        {
            currentBarIndex = -1;
        }

        int targetBarIndex = currentBarIndex + direction;

        // Clamp the target index
        if (targetBarIndex < 0)
        {
            targetBarIndex = 0;
        }
        else if (targetBarIndex >= barTimes.Count)
        {
            targetBarIndex = barTimes.Count - 1;
        }

        // Get the target time
        float targetTime = barTimes[targetBarIndex];

        // Jump to the target time
        psarcLoader.JumpToTime(targetTime);
    }

    void JumpByTime(float timeDelta)
    {
        if (psarcLoader?.audioSource?.clip == null) return;

        float targetTime = psarcLoader.audioSource.time + timeDelta;
        
        // Clamp the target time
        targetTime = Mathf.Clamp(targetTime, 0f, psarcLoader.audioSource.clip.length);

        psarcLoader.JumpToTime(targetTime);
    }
}
