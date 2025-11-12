// Assets/Scripts/NoteData.cs
using UnityEngine;

public class NoteData
{
    public float time;           // When the note should be played (seconds)
    public int stringNumber;     // Which string (e.g. 0 = lowest, 5 = highest for bass or guitar)
    public int fretNumber;       // Which fret to press
    public float sustain;      // Duration
    public bool palmMute;      // Palm Mute flag
    public bool mute;          // Muted note flag
    public bool accent;        // Accent flag
    public int slideTo = -1;        // Slide target (-1 if none)
    public bool hammerOn;      // Hammer-on
    public bool pullOff;       // Pull-off

    // Fields for NoteHighway to track the note's state
    public GameObject noteObject;
    public GameObject fretLabelObject; // New field to track the separate fret label object
    public bool isSpawned = false;
    public bool hitMarkerSpawned = false; // Track if the hit marker has been spawned
    public float previousZPos = -1f;      // Track the last Z position to detect line crossing
}