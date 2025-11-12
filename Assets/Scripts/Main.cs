using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem; // Required for the new Input System

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

    private bool isPlaying = false;
    private float _initialVideoOffsetMs = 0f; // Store the default value from NoteHighway.cs on startup
    private bool isUpdatingScrubberFromCode = false; // Flag to prevent feedback loop

    void Start()
    {
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
            songSpeedSlider.value = 70f; // Hardcoded to 70%
            UpdateSongSpeedLabel(70f);
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

        // Keyboard controls using the new Input System
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TogglePlayPause();
        }

        if (Keyboard.current.mKey.wasPressedThisFrame)
        {
            ToggleMute();
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

            // 2. Update play/pause button text based on actual playback state (fixes autoplay bug)
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

        psarcLoader.audioSource.time = value * psarcLoader.audioSource.clip.length;
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
            videoOffsetLabel.text = $"Video Offset: {offsetMs:F0}ms";
        }
    }
}
