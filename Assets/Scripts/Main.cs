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

    public bool runInBackground = true; // Option to keep the game running when focus is lost

    private bool isPlaying = false;
    private float _initialVideoOffsetMs = 0f; // Store the default value from NoteHighway.cs on startup
    private float _appliedSongSpeed = 100f; // Store the currently applied song speed
    private bool isUpdatingScrubberFromCode = false; // Flag to prevent feedback loop
    private bool isUpdatingSongSpeedFromCode = false; // Flag to prevent feedback loop for song speed slider

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
        // Check if playback has stopped naturally (e.g., reached end of song)
        if (psarcLoader?.audioSource?.clip != null && isPlaying && !psarcLoader.audioSource.isPlaying)
        {
            isPlaying = false;
            UpdatePlayPauseButtonText();
        }

        // Ensure a keyboard is present before checking for input
        if (Keyboard.current == null) return;

        // Keyboard controls using the new Input System
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TogglePlayPause();
        }

        if (Keyboard.current.mKey.wasPressedThisFrame)
        {
            ToggleMute();
        }

        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            NavigateByBar(-1);
        }

        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            NavigateByBar(1);
        }

        if (psarcLoader?.audioSource?.clip != null && psarcLoader.audioSource.isPlaying)
        {
            // Set flag to prevent OnScrubberValueChanged from firing the reset logic
            isUpdatingScrubberFromCode = true;
            playbackScrubber.value = psarcLoader.audioSource.time / psarcLoader.audioSource.clip.length;
            isUpdatingScrubberFromCode = false;
        }
    }

    public void NewSong()
    {
        if (psarcLoader != null)
        {
            psarcLoader.NewSong();
            
            // 1. Apply default video offset
            if (videoOffsetSlider != null && noteHighway != null)
            {
                videoOffsetSlider.value = _initialVideoOffsetMs;
                noteHighway.videoOffsetMs = _initialVideoOffsetMs;
                UpdateVideoOffsetLabel(_initialVideoOffsetMs); // Update label on song load
            }

            // 2. Reset song speed
            if (songSpeedSlider != null)
            {
                const float defaultSpeed = 100f;
                songSpeedSlider.value = defaultSpeed;
                _appliedSongSpeed = defaultSpeed; // Reset applied speed tracker
                psarcLoader.SetSongSpeed(defaultSpeed);
                UpdateSongSpeedLabel(defaultSpeed);
                
                // Ensure the apply button is hidden after reset
                if (applySpeedButton != null)
                {
                    applySpeedButton.gameObject.SetActive(false);
                }
            }

            // 3. Update play/pause button text based on actual playback state (fixes autoplay bug)
            isPlaying = psarcLoader.audioSource != null && psarcLoader.audioSource.isPlaying;
            UpdatePlayPauseButtonText();
        }
    }

    void TogglePlayPause()
    {
        if (psarcLoader == null || psarcLoader.audioSource == null) return;

        isPlaying = !psarcLoader.audioSource.isPlaying;

        if (isPlaying)
        {
            psarcLoader.audioSource.Play();
        }
        else
        {
            psarcLoader.audioSource.Pause();
        }
        UpdatePlayPauseButtonText();
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

        if (psarcLoader?.audioSource?.clip == null) return;

        float targetTime = value * psarcLoader.audioSource.clip.length;
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
            psarcLoader.SetSongSpeed(newSpeed);
            _appliedSongSpeed = newSpeed;
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
}
