using UnityEngine;
using System.Collections.Generic;
using TMPro; // Assuming TextMeshPro is used for the fret number label

public class NoteHighway : MonoBehaviour
{
    public GameObject notePrefab;               // Drag your prefab here in Editor
    public AudioSource audioSource;             // Assign the AudioSource here or in Start()
    public List<NoteData> notes;                // Fill this with ArrangementParser
    public float videoOffsetMs = 0f;            // Video offset in milliseconds (from PsarcLoader)
    public float spawnZ = 100f;                 // Starting Z position for notes (far end of highway)
    public float hitZ = 0f;                     // Where notes meet the play line (near end)
    public float despawnZOffset = -30f;         // Where notes meet the play line (near end)
    public bool devNotePositioningMode = false; // If true, notes will not despawn and will stop at hitZ
    public float noteTravelTime = 50f;          // How long notes take to reach hit line
    public Main main;                           // Reference to the Main script for scrubbing state

    [Header("Fret Label")]
    public GameObject fretLabelPrefab;       // Prefab for the fret number text label
    public float fretLabelYOffset = -5.0f;   // Vertical offset below the lowest string
    public float fretLabelXOffset = 0.0f;    // Horizontal offset for the fret number text label
    public float fretLabelRotationX = 0.0f;  // X-axis rotation for the fret number text label (e.g., 90 for flat)
    public float fretLabelFontSize = 10.0f;  // Font size for the fret number text label
    public float fretLabelYScale = 1.0f;     // Vertical scale for the fret number text label

    [Header("Note Visuals")]
    public float noteScaleX = 1f;
    public float noteScaleY = 1f;
    public float noteScaleZ = 1f;
    public float zeroFretNoteWidth = 25.0f; // Width for open/zero-fret notes (e.g., 5 frets wide)
    public float zeroFretNoteHeight = 0.5f; // Height for open/zero-fret notes
    public float zeroFretNoteXOffset = 0f; // Horizontal offset for open/zero-fret notes

    [Header("Hit Marker Visuals")]
    public float hitMarkerFadeTime = 0.5f; // Time in seconds for the hit marker to fade
    public Material[] hitMarkerMaterials; // String-specific materials for the glowing hit marker

    [Header("3D Positioning")]
    public int totalStrings = 4;
    public float fretboardHeight = 10.0f; // Total height of the fretboard area
    public float fretboardYOffset = 0.0f; // Vertical offset for the entire fretboard
    public float laneSpacing = 10.0f;
    public float fretSpacing = 5.0f;
    public float fretOffset = -40f;

    [Header("Fretboard String Visuals")]
    public float stringWidth = 80f;
    public float stringHeight = 0.3f;
    public float stringDepth = 1f;

    [Header("Fretboard Visuals")]
    public Material fretLineMaterial;
    public float fretLineThickness = 0.5f;
    public float fretLineDepth = 0.1f;
    public float fretboardLinesZYOffset = -3.0f;
    public float fretboardLinesZHeight = 0.5f; // New field for the tiny height
    public int maxFretNumber = 24;

    [Header("Inlay Dots")]
    public Material inlayDotMaterial;
    public float inlayDotScale = 1.0f;
    public float doubleDotOffset = 1.5f; // Vertical offset for the 12th fret double dot
    public int[] inlayFrets = { 3, 5, 7, 9, 12, 15, 17, 19, 21 };

    void Start()
    {
        // Do NOT log warnings about notes/audioSource here!
        CreateFretboardStrings();
        CreateFretboardVisuals();
        CreateFretboardLinesZ(); // New call to create lines extending down the highway
    }

    // 1. Lane Positioning Functions
    // String to Y (vertical):
    float GetStringY(int stringNumber) {
        // String 0 is highest Y, string N is lowest Y
        return (totalStrings - 1 - stringNumber) * laneSpacing;
    }

    // Fret to X (horizontal):
    float GetFretX(int fretNumber) {
        // fretOffset centers the fretboard (adjust for your display)
        return fretOffset + fretNumber * fretSpacing;
    }

    [Header("Materials")]
    public Material[] stringMaterials;

    private void CreateFretboardLinesZ()
    {
        // Calculate the total height of the fretboard visuals
        // String 0 is at GetStringY(0), last string is at GetStringY(totalStrings - 1)
        float fretboardLinesZY = GetStringY(totalStrings - 1) + fretboardLinesZYOffset;

        // The length of the line is from hitZ to spawnZ
        float lineLength = spawnZ - hitZ;
        float lineCenterZ = hitZ + (lineLength / 2f);

        // Create a line for each fret position (where the notes are centered)
        for (int fret = 1; fret <= maxFretNumber; fret++)
        {
            // Create the line object (using a simple cube primitive)
            GameObject fretLineZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fretLineZ.name = $"FretLineZ_{fret}";
            fretLineZ.transform.SetParent(transform); // Parent to NoteHighway for organization

            
            fretLineZ.transform.position = new Vector3(
                GetFretX(fret) - (fretSpacing / 2f),
                fretboardLinesZY,
                lineCenterZ
            );

            // Scaling: X is thickness, Y is total height, Z is the length of the highway
            fretLineZ.transform.localScale = new Vector3(
                fretLineThickness,
                fretboardLinesZHeight, // Use the new public field for height
                lineLength
            );

            // Material
            Renderer renderer = fretLineZ.GetComponent<Renderer>();
            if (renderer != null && fretLineMaterial != null)
            {
                renderer.material = fretLineMaterial;
            }
            
            // Optional: Disable colliders if they are not needed
            Collider collider = fretLineZ.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }
    }

    void Update() {
        if (audioSource == null || notes == null || notes.Count == 0)
        {
            // Wait for PsarcLoader to assign everything before running visual code
            return;
        }

        if (audioSource == null) return;
        float t = audioSource.time;
        
        // Determine the effective despawn Z and total travel time based on the dev mode
        float effectiveDespawnZ = devNotePositioningMode ? hitZ : despawnZOffset;
        float totalTravelTime;

        if (devNotePositioningMode)
        {
            // If in dev mode, notes stop at hitZ, so total travel time is just noteTravelTime
            totalTravelTime = noteTravelTime;
        }
        else
        {
            // Calculate the total time for a note to travel from spawnZ to effectiveDespawnZ (-2f)
            float speed = (spawnZ - hitZ) / noteTravelTime;
            float timeToDespawn = (hitZ - effectiveDespawnZ) / speed;
            totalTravelTime = noteTravelTime + timeToDespawn;
        }

        foreach (var note in notes) {
            // Apply the video offset (converted to seconds) to the note's time
            // The offset is inverted to match the user's desired behavior.
            float offsetSeconds = -videoOffsetMs / 1000f;
            float startTime = note.time - noteTravelTime - offsetSeconds;

            // REWIND/RESET LOGIC: If music time goes before the note's start time, reset the note's state.
            // The 'note.isSpawned' check was removed to ensure notes that were previously hit (and thus
            // not spawned) are also reset, allowing them to be re-spawned and hit again after a rewind.
            if (t < startTime)
            {
                if (note.noteObject != null)
                {
                    Destroy(note.noteObject);
                    note.noteObject = null;
                }
                if (note.fretLabelObject != null)
                {
                    Destroy(note.fretLabelObject);
                    note.fretLabelObject = null;
                }
                note.isSpawned = false;
                note.hitMarkerSpawned = false;
                note.previousZPos = -1f; // Reset previous position
            }

            // 1. Spawning Logic: Spawn the note when it's time to start moving and it's not already spawned
            if (!note.isSpawned && t >= startTime && t < startTime + totalTravelTime)
            {
                float noteX;
                float scaleX;
                float scaleY;

                if (note.fretNumber == 0)
                {
                    // Open string note: make it a long line
                    noteX = fretOffset + zeroFretNoteWidth / 2f + zeroFretNoteXOffset;
                    scaleX = zeroFretNoteWidth;
                    scaleY = zeroFretNoteHeight; // Use the correct height for open notes
                }
                else
                {
                    // Fretted note: use standard positioning and scale
                    noteX = GetFretX(note.fretNumber);
                    scaleX = noteScaleX;
                    scaleY = noteScaleY;
                }

                // 2. NotePrefab Instantiation (using 3D coordinates)
                Vector3 spawnPos = new Vector3(
                    noteX,                          // X: calculated position
                    GetStringY(note.stringNumber),  // Y: string lane
                    spawnZ                          // Z: depth ("time until hit")
                );
                var go = Instantiate(notePrefab, spawnPos, Quaternion.identity, transform);
                go.name = $"Note_{note.time:F2}_{note.stringNumber}_{note.fretNumber}";
                
                // Apply scale
                go.transform.localScale = new Vector3(scaleX, scaleY, noteScaleZ);

                note.noteObject = go;
                note.isSpawned = true;
                note.previousZPos = spawnZ; // Initialize previous position on spawn

                // Set Note Material based on string
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null && stringMaterials != null && note.stringNumber < stringMaterials.Length)
                {
                    renderer.material = stringMaterials[note.stringNumber];
                }

                // Show Fret Numbers (Option 1: Text Label)
                if (fretLabelPrefab != null)
                {
                    // Calculate the fixed Y position for the label (below the lowest string)
                    // totalStrings - 1 is the index of the lowest string
                    float fixedLabelY = GetStringY(totalStrings - 1) + fretLabelYOffset;
                    
                    Vector3 labelPos = new Vector3(
                        spawnPos.x + fretLabelXOffset, // Apply the new X offset
                        fixedLabelY,                   // Use the fixed Y position
                        spawnZ
                    );

                    // Instantiate the label and parent it to the NoteHighway transform (this.transform)
                    // so it moves along Z with the note but stays at a constant Y.
                    var labelGO = Instantiate(fretLabelPrefab, labelPos, Quaternion.identity, transform);
                    labelGO.name = $"FretLabel_{note.fretNumber}_{note.stringNumber}";
                    
                    // Store the label object in the NoteData so it can be destroyed later
                    note.fretLabelObject = labelGO;

                    // Find the TextMeshPro component on the label prefab
                    var labelText = labelGO.GetComponent<TextMeshPro>();
                    if (labelText != null)
                    {
                        // Apply the desired rotation to the TextMeshPro component's transform
                        labelText.transform.localRotation = Quaternion.Euler(fretLabelRotationX, 0, 0);
                        
                        labelText.text = note.fretNumber.ToString();
                        labelText.fontSize = fretLabelFontSize; // Apply font size
                    }
                    else
                    {
                        // Apply the rotation to the root object if no TextMeshPro component is found
                        labelGO.transform.localRotation = Quaternion.Euler(fretLabelRotationX, 0, 0);
                        Debug.LogWarning("Fret Label Prefab does not contain a TextMeshPro component.");
                    }
                }
            }

            // 2. Movement and Despawning Logic: Only for spawned notes
            if (note.noteObject != null)
            {
                float timeSinceSpawn = t - startTime;
                
                // Progress from spawnZ (0) to effectiveDespawnZ (1)
                float totalProgress = Mathf.Clamp01(timeSinceSpawn / totalTravelTime);
                
                float zPos = Mathf.Lerp(spawnZ, effectiveDespawnZ, totalProgress);
                
                // To fix Y-level drift, recalculate the correct Y position every frame.
                float correctY = GetStringY(note.stringNumber);
                note.noteObject.transform.position = new Vector3(note.noteObject.transform.position.x, correctY, zPos);

                // Move the associated fret label along the Z-axis
                if (note.fretLabelObject != null)
                {
                    // Keep the label's X and Y fixed, only update Z
                    Vector3 labelPos = note.fretLabelObject.transform.position;
                    note.fretLabelObject.transform.position = new Vector3(labelPos.x, labelPos.y, zPos);
                }

                // Hit Marker Spawning Logic based on crossing the hit line
                if (note.previousZPos > hitZ && zPos <= hitZ && !note.hitMarkerSpawned)
                {
                    // To guarantee perfect alignment, calculate the exact position for the hit marker
                    Vector3 hitPosition = new Vector3(
                        note.noteObject.transform.position.x, // X position is correct
                        GetStringY(note.stringNumber),        // Explicitly set the correct Y
                        hitZ                                  // Explicitly set the correct Z
                    );

                    // Spawn the hit marker at the calculated perfect position
                    GameObject hitMarker = Instantiate(note.noteObject, hitPosition, Quaternion.identity, transform);
                    hitMarker.name = $"HitMarker_{note.time:F2}_{note.stringNumber}_{note.fretNumber}";
                    hitMarker.transform.localScale *= 1.2f; // Apply scale pop

                    Renderer hitMarkerRenderer = hitMarker.GetComponent<Renderer>();
                    if (hitMarkerRenderer != null && hitMarkerMaterials != null && note.stringNumber < hitMarkerMaterials.Length)
                    {
                        Material hitMatInstance = new Material(hitMarkerMaterials[note.stringNumber]);
                        hitMarkerRenderer.material = hitMatInstance;
                    }

                    hitMarker.AddComponent<HitMarkerFader>().fadeTime = hitMarkerFadeTime;

                    // The original note has served its purpose. Hide it and destroy it.
                    note.noteObject.SetActive(false);
                    Destroy(note.noteObject);
                    note.noteObject = null; // Important to prevent further processing
                    note.isSpawned = false; // Reset state for rewind
                    note.hitMarkerSpawned = true; // Mark as hit for this pass

                    // Destroy the fret label object as well
                    if (note.fretLabelObject != null)
                    {
                        Destroy(note.fretLabelObject);
                        note.fretLabelObject = null;
                    }
                }
                // If the note has passed the despawn point (and wasn't destroyed by the hit marker logic)
                else if (!devNotePositioningMode && timeSinceSpawn >= totalTravelTime)
                {
                    Destroy(note.noteObject);
                    note.noteObject = null;
                    note.isSpawned = false; // Reset state for rewind

                    // Destroy the fret label object as well
                    if (note.fretLabelObject != null)
                    {
                        Destroy(note.fretLabelObject);
                        note.fretLabelObject = null;
                    }
                }

                // Update the previous position for the next frame's check
                note.previousZPos = zPos;
            }
        }
    }

    public void ResetNotes()
    {
        if (notes == null) return;

        foreach (var note in notes)
        {
            if (note.noteObject != null)
            {
                Destroy(note.noteObject);
                note.noteObject = null;
            }
            if (note.fretLabelObject != null)
            {
                Destroy(note.fretLabelObject);
                note.fretLabelObject = null;
            }
            note.isSpawned = false;
            note.hitMarkerSpawned = false;
            note.previousZPos = -1f; // Reset previous position
        }

        // Crucial: Set notes to null so the Update loop stops processing immediately
        notes = null;
    }

    private void CreateFretboardStrings()
    {
        // Check if we have materials and string count set up
        if (stringMaterials == null || stringMaterials.Length == 0 || totalStrings <= 0)
        {
            Debug.LogWarning("Cannot create fretboard strings: stringMaterials is null/empty or totalStrings is zero.");
            return;
        }

        for (int i = 0; i < totalStrings; i++)
        {
            // 1. Create the string object (using a simple cube primitive)
            GameObject stringObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stringObject.name = $"FretboardString_{i}";
            stringObject.transform.SetParent(transform); // Parent to NoteHighway for organization

            // 2. Positioning
            float stringY = GetStringY(i) + fretboardYOffset;
            // X is centered (0)
            stringObject.transform.position = new Vector3(
                0f, // Centered on X
                stringY,
                hitZ // Positioned at the hit line
            );

            // 3. Scaling
            stringObject.transform.localScale = new Vector3(
                stringWidth, // X width
                stringHeight, // Y height
                stringDepth // Z depth
            );

            // 4. Material
            if (i < stringMaterials.Length)
            {
                Renderer renderer = stringObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = stringMaterials[i];
                }
            }
            
            // Optional: Disable colliders if they are not needed
            Collider collider = stringObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }
    }

    private void CreateFretboardVisuals()
    {
        // Calculate the total height of the fretboard visuals
        // String 0 is at GetStringY(0), last string is at GetStringY(totalStrings - 1)
        float padding = 10.0f; // 5 units above and 5 units below
        float totalHeight = GetStringY(0) - GetStringY(totalStrings - 1) + stringHeight + padding; // Add string height and padding for full coverage
        float verticalCenterY = GetStringY(totalStrings - 1) + (totalHeight / 2f) - (stringHeight / 2f) - (padding / 2f); // Adjust center to account for padding

        // 1. Create Fret Lines
        // Start at fret 1 to skip the zero fret (the nut)
        for (int fret = 1; fret <= maxFretNumber; fret++)
        {
            // Create the line object (using a simple cube primitive)
            GameObject fretLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fretLine.name = $"FretLine_{fret}";
            fretLine.transform.SetParent(transform);

            // Positioning: X at the fret position, Y centered, Z at hitZ
            // Shift the fret line back by half a fret space to act as the boundary before the note's center
            fretLine.transform.position = new Vector3(
                GetFretX(fret) - (fretSpacing / 2f),
                verticalCenterY,
                hitZ
            );

            // Scaling: X is thickness, Y is total height, Z is depth
            fretLine.transform.localScale = new Vector3(
                fretLineThickness,
                totalHeight,
                fretLineDepth
            );

            // Material
            Renderer renderer = fretLine.GetComponent<Renderer>();
            if (renderer != null && fretLineMaterial != null)
            {
                renderer.material = fretLineMaterial;
            }

            // Disable collider
            Collider collider = fretLine.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        // 2. Create Inlay Dots
        if (inlayDotMaterial != null)
        {
            foreach (int fret in inlayFrets)
            {
                if (fret > 0 && fret <= maxFretNumber)
                {
                    // Special case for 12th fret (double dot)
                    int dotCount = (fret == 12) ? 2 : 1;
                    
                    // The dot should be centered on the fret space, which is where the notes are.
                    float fretCenterX = GetFretX(fret); // Use the note's center position

                    for (int i = 0; i < dotCount; i++)
                    {
                        // Create the dot object (using a simple cube primitive for a flat, disc-like appearance)
                        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        dot.name = $"InlayDot_{fret}_{i}";
                        dot.transform.SetParent(transform);

                        // Vertical offset for double dot at 12th fret
                        float dotOffsetY = 0f;
                        if (dotCount == 2)
                        {
                            // Place dots slightly above and below the center line
                            dotOffsetY = (i == 0) ? doubleDotOffset : -doubleDotOffset;
                        }

                        // Positioning: X at fret center, Y at vertical center (with offset), Z at hitZ
                        dot.transform.position = new Vector3(
                            fretCenterX,
                            verticalCenterY + dotOffsetY,
                            hitZ
                        );

                        // Scaling: Make it a flat disc/square
                        dot.transform.localScale = new Vector3(
                            inlayDotScale,
                            inlayDotScale,
                            fretLineDepth * 2f // Give it a small depth so it's visible in 3D
                        );

                        // Material
                        Renderer renderer = dot.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            renderer.material = inlayDotMaterial;
                        }

                        // Disable collider
                        Collider collider = dot.GetComponent<Collider>();
                        if (collider != null)
                        {
                            collider.enabled = false;
                        }
                    }
                }
            }
        }
    }
}