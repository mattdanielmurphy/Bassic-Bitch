
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

    private bool isPlaying = false;
    public bool isDraggingScrubber = false;
    private bool isUpdatingScrubberFromCode = false; // Flag to prevent feedback loop

    void Start()
    {
        playPauseButton.onClick.AddListener(TogglePlayPause);
        muteButton.onClick.AddListener(ToggleMute);
        playbackScrubber.onValueChanged.AddListener(OnScrubberValueChanged);

        // Set initial mute button text based on PsarcLoader's setting
        if (psarcLoader != null)
        {
            UpdateMuteButtonText();
        }

        var eventTrigger = playbackScrubber.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var pointerDown = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown };
        pointerDown.callback.AddListener((data) => { isDraggingScrubber = true; });
        eventTrigger.triggers.Add(pointerDown);

        var pointerUp = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
        pointerUp.callback.AddListener((data) => { isDraggingScrubber = false; });
        eventTrigger.triggers.Add(pointerUp);
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

        if (psarcLoader?.audioSource?.clip != null && psarcLoader.audioSource.isPlaying && !isDraggingScrubber)
        {
            // Set flag to prevent OnScrubberValueChanged from firing the reset logic
            isUpdatingScrubberFromCode = true;
            playbackScrubber.value = psarcLoader.audioSource.time / psarcLoader.audioSource.clip.length;
            isUpdatingScrubberFromCode = false;
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

        if (noteHighway != null)
        {
            noteHighway.ResetNotes();
        }
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
}
