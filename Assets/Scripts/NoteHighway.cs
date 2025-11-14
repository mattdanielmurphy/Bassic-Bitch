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
    public PsarcLoader psarcLoader;             // Reference to PsarcLoader for song speed
    
    // Private list for bar times, managed via public method
    private List<float> barTimes;
    // New list to manage our dynamic bar markers
    private List<BarMarkerData> barMarkers = new List<BarMarkerData>();


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

    [Header("Bar Marker Visuals")]
    public Material barMarkerMaterial;
    // Horizontal Line (X-axis)
    public float barLineYPosition = -3.0f;      // Y position for the horizontal bar line (default to fretboardLinesZYOffset)
    public float barLineThickness = 0.5f;       // Thickness of the bar line on the Y-axis
    public float fretboardBarLineTotalWidth = 80f; // Total width of the bar line on the X-axis (default to stringWidth)
    public float barLineXCenterOffset = 0f;     // X offset for centering the horizontal line

    // Label Positioning
    public float barLabelXOffsetFromStart = -10.0f; // Horizontal offset for the label from the start of the line
    public float barLabelYOffsetAboveLine = 2.0f;   // Vertical offset for the label above the line
    public TextAlignmentOptions barLabelTextAlignment = TextAlignmentOptions.MidlineRight; // Alignment for the bar number label

    // Left Bar Marker Line (Y-axis)
    public float leftBarMarkerLineHeight = 5.0f;    // Height of the new Y-axis line
    public float leftBarMarkerLineThickness = 0.5f; // Thickness of the new Y-axis line
    public float leftBarMarkerLineXOffset = -5.0f;  // X offset for the left bar marker line from the start of the horizontal bar line

    [Header("Inlay Dots")]
    public Material inlayDotMaterial;
    public float inlayDotScale = 1.0f;
    public float doubleDotOffset = 1.5f; // Vertical offset for the 12th fret double dot
    public int[] inlayFrets = { 3, 5, 7, 9, 12, 15, 17, 19, 21 };

    void Start()
    {
        // Calculate the width of the fretboard and set it to the bar line width
        // The width is calculated from the center of fret 0 to the center of maxFretNumber.
        fretboardBarLineTotalWidth = maxFretNumber * fretSpacing;

        // Do NOT log warnings about notes/audioSource here!
        CreateFretboardStrings();
        CreateFretboardVisuals();
        CreateFretboardLinesZ(); // New call to create lines extending down the highway
    }
    
    /// <summary>
    /// Receives bar times from PsarcLoader and populates the bar marker data.
    /// </summary>
    public void SetBarTimes(List<float> newBarTimes)
    {
        this.barTimes = newBarTimes;
        barMarkers.Clear(); // Clear any old data

        if (this.barTimes == null) return;
        
        for (int i = 0; i < this.barTimes.Count; i++)
        {
            barMarkers.Add(new BarMarkerData
            {
                time = this.barTimes[i],
                barNumber = i + 1 // Bar numbers are 1-based
            });
        }
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
    public Material palmMuteMaterial;
    public Material muteMaterial;
    public Material accentMaterial;
    public Material slideMaterial;
    public Material hammerOnMaterial;
    public Material pullOffMaterial;

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
    
    // DELETED: The old CreateBarMarkers() method is no longer needed.

    void Update() {
        if (audioSource == null)
        {
            // This is normal before a song is loaded.
            return;
        }
        if (psarcLoader == null)
        {
            UnityEngine.Debug.LogWarning("NoteHighway Update: psarcLoader is null. Waiting for PsarcLoader to assign.");
            return;
        }

        float t = audioSource.time;
        
        // Calculate adjusted note travel time based on song speed
        float currentSongSpeedPercentage = psarcLoader.currentSongSpeedPercentage;
        float adjustedNoteTravelTime = noteTravelTime / (currentSongSpeedPercentage / 100f);

        // Determine the effective despawn Z and total travel time based on the dev mode
        float effectiveDespawnZ = devNotePositioningMode ? hitZ : despawnZOffset;
        float speed = (spawnZ - hitZ) / adjustedNoteTravelTime;
        float timeToDespawn = (hitZ - effectiveDespawnZ) / speed;
        float totalTravelTime = adjustedNoteTravelTime + timeToDespawn;

        // Calculate the video offset (converted to seconds) once for both notes and bar markers.
        // The offset is inverted to match the user's desired behavior.
        float offsetSeconds = -videoOffsetMs / 1000f;

        // --- NEW Bar Marker Spawning and Movement Logic ---
        if (barMarkers != null)
        {
            foreach (var bar in barMarkers)
            {
                float adjustedBarTime = bar.time / (currentSongSpeedPercentage / 100f);
                float startTime = adjustedBarTime - adjustedNoteTravelTime - offsetSeconds;

                // Rewind/Reset Logic
                if (t < startTime && bar.isSpawned)
                {
                    if (bar.markerObject != null) Destroy(bar.markerObject);
                    if (bar.labelObject != null) Destroy(bar.labelObject);
                    if (bar.perpendicularObject != null) Destroy(bar.perpendicularObject);
                    bar.isSpawned = false;
                }

                // Spawning Logic
                if (!bar.isSpawned && t >= startTime && t < startTime + totalTravelTime)
                {
                    // Calculate positions
                    float lineStartX = barLineXCenterOffset - (fretboardBarLineTotalWidth / 2f);
                    float lineCenterY = barLineYPosition;
                    
                    // 1. Create the visual horizontal line (X-axis)
                    GameObject markerLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    markerLine.name = $"BarMarkerLine_{bar.barNumber}";
                    markerLine.transform.SetParent(transform);
                    markerLine.transform.position = new Vector3(barLineXCenterOffset, lineCenterY, spawnZ);
                    markerLine.transform.localScale = new Vector3(fretboardBarLineTotalWidth, barLineThickness, 1f);
                    
                    Renderer renderer = markerLine.GetComponent<Renderer>();
                    if (renderer != null && barMarkerMaterial != null) renderer.material = barMarkerMaterial;
                    Collider collider = markerLine.GetComponent<Collider>();
                    if (collider != null) collider.enabled = false;
                    
                    bar.markerObject = markerLine;

                    // 2. Create the left bar marker line (Y-axis)
                    float leftLineX = lineStartX + leftBarMarkerLineXOffset;
                    float leftLineCenterY = lineCenterY + (leftBarMarkerLineHeight / 2f); // Center is above the horizontal line
                    
                    GameObject perpLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    perpLine.name = $"LeftBarMarkerLine_{bar.barNumber}";
                    perpLine.transform.SetParent(transform);
                    perpLine.transform.position = new Vector3(leftLineX, leftLineCenterY, spawnZ);
                    perpLine.transform.localScale = new Vector3(leftBarMarkerLineThickness, leftBarMarkerLineHeight, 1f);

                    Renderer perpRenderer = perpLine.GetComponent<Renderer>();
                    if (perpRenderer != null && barMarkerMaterial != null) perpRenderer.material = barMarkerMaterial;
                    Collider perpCollider = perpLine.GetComponent<Collider>();
                    if (perpCollider != null) perpCollider.enabled = false;
                    
                    bar.perpendicularObject = perpLine;

                    // 3. Create the text label
                    if (fretLabelPrefab != null)
                    {
                        float labelX = lineStartX + barLabelXOffsetFromStart;
                        float labelY = lineCenterY + (barLineThickness / 2f) + barLabelYOffsetAboveLine;
                        Vector3 labelPos = new Vector3(labelX, labelY, spawnZ);
                        
                        GameObject labelGO = Instantiate(fretLabelPrefab, labelPos, Quaternion.identity, transform);
                        labelGO.name = $"BarLabel_{bar.barNumber}";
                        
                        var labelText = labelGO.GetComponent<TextMeshPro>();
                        if (labelText != null)
                        {
                            labelText.transform.localRotation = Quaternion.Euler(fretLabelRotationX, 0, 0);
                            labelText.text = bar.barNumber.ToString();
                            labelText.fontSize = fretLabelFontSize;
                            labelText.alignment = barLabelTextAlignment; // Apply the new alignment
                        }
                        bar.labelObject = labelGO;
                    }
                    bar.isSpawned = true;
                }

                // Movement and Despawning Logic
                if (bar.isSpawned)
                {
                    float timeSinceSpawn = t - startTime;
                    float progress = Mathf.Clamp01(timeSinceSpawn / totalTravelTime);
                    float zPos = Mathf.Lerp(spawnZ, effectiveDespawnZ, progress);

                    if (bar.markerObject != null) bar.markerObject.transform.position = new Vector3(bar.markerObject.transform.position.x, bar.markerObject.transform.position.y, zPos);
                    if (bar.labelObject != null) bar.labelObject.transform.position = new Vector3(bar.labelObject.transform.position.x, bar.labelObject.transform.position.y, zPos);
                    if (bar.perpendicularObject != null) bar.perpendicularObject.transform.position = new Vector3(bar.perpendicularObject.transform.position.x, bar.perpendicularObject.transform.position.y, zPos);

                    if (timeSinceSpawn >= totalTravelTime)
                    {
                        if (bar.markerObject != null) Destroy(bar.markerObject);
                        if (bar.labelObject != null) Destroy(bar.labelObject);
                        if (bar.perpendicularObject != null) Destroy(bar.perpendicularObject);
                        bar.isSpawned = false;
                    }
                }
            }
        }
        // --- End of NEW Bar Marker Logic ---

        if (notes == null) return; // Exit if no notes are loaded

        foreach (var note in notes) {
            // Apply the video offset (converted to seconds) to the note's time
            // The offset is inverted to match the user's desired behavior.
            // Adjust note.time based on the current song speed percentage
            float adjustedNoteTime = note.time / (psarcLoader.currentSongSpeedPercentage / 100f);
            float startTime = adjustedNoteTime - adjustedNoteTravelTime - offsetSeconds;
            
            // REWIND/RESET LOGIC:
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

            // 1. Spawning Logic:
            if (!note.isSpawned && t >= startTime && t < startTime + totalTravelTime)
            {
                float noteX;
                float scaleX;
                float scaleY;

                if (note.fretNumber == 0)
                {
                    noteX = fretOffset + zeroFretNoteWidth / 2f + zeroFretNoteXOffset;
                    scaleX = zeroFretNoteWidth;
                    scaleY = zeroFretNoteHeight;
                }
                else
                {
                    noteX = GetFretX(note.fretNumber);
                    scaleX = noteScaleX;
                    scaleY = noteScaleY;
                }

                Vector3 spawnPos = new Vector3(
                    noteX,
                    GetStringY(note.stringNumber),
                    spawnZ
                );
                var go = Instantiate(notePrefab, spawnPos, Quaternion.identity, transform);
                go.name = $"Note_{note.time:F2}_{note.stringNumber}_{note.fretNumber}";
                
                go.transform.localScale = new Vector3(scaleX, scaleY, noteScaleZ);
                
                note.noteObject = go;
                note.isSpawned = true;
                note.previousZPos = spawnZ;

                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (note.palmMute && palmMuteMaterial != null) renderer.material = palmMuteMaterial;
                    else if (note.mute && muteMaterial != null) renderer.material = muteMaterial;
                    else if (note.accent && accentMaterial != null) renderer.material = accentMaterial;
                    else if (note.hammerOn && hammerOnMaterial != null) renderer.material = hammerOnMaterial;
                    else if (note.pullOff && pullOffMaterial != null) renderer.material = pullOffMaterial;
                    else if (note.slideTo != -1 && slideMaterial != null) renderer.material = slideMaterial;
                    else if (stringMaterials != null && note.stringNumber < stringMaterials.Length) renderer.material = stringMaterials[note.stringNumber];
                }

                if (fretLabelPrefab != null)
                {
                    float fixedLabelY = GetStringY(totalStrings - 1) + fretLabelYOffset;
                    Vector3 labelPos = new Vector3(spawnPos.x + fretLabelXOffset, fixedLabelY, spawnZ);
                    var labelGO = Instantiate(fretLabelPrefab, labelPos, Quaternion.identity, transform);
                    labelGO.name = $"FretLabel_{note.fretNumber}_{note.stringNumber}";
                    note.fretLabelObject = labelGO;

                    var labelText = labelGO.GetComponent<TextMeshPro>();
                    if (labelText != null)
                    {
                        labelText.transform.localRotation = Quaternion.Euler(fretLabelRotationX, 0, 0);
                        labelText.text = note.fretNumber.ToString();
                        labelText.fontSize = fretLabelFontSize;
                    }
                    else
                    {
                        labelGO.transform.localRotation = Quaternion.Euler(fretLabelRotationX, 0, 0);
                        Debug.LogWarning("Fret Label Prefab does not contain a TextMeshPro component.");
                    }
                }
            }

            // 2. Movement and Despawning Logic:
            if (note.noteObject != null)
            {
                float timeSinceSpawn = t - startTime;
                float totalProgress = Mathf.Clamp01(timeSinceSpawn / totalTravelTime);
                float zPos = Mathf.Lerp(spawnZ, effectiveDespawnZ, totalProgress);
                
                float correctY = GetStringY(note.stringNumber);
                note.noteObject.transform.position = new Vector3(note.noteObject.transform.position.x, correctY, zPos);

                if (note.fretLabelObject != null)
                {
                    Vector3 labelPos = note.fretLabelObject.transform.position;
                    note.fretLabelObject.transform.position = new Vector3(labelPos.x, labelPos.y, zPos);
                }

                if (note.previousZPos > hitZ && zPos <= hitZ && !note.hitMarkerSpawned)
                {
                    Vector3 hitPosition = new Vector3(
                        note.noteObject.transform.position.x,
                        GetStringY(note.stringNumber),
                        hitZ
                    );

                    GameObject hitMarker = Instantiate(note.noteObject, hitPosition, Quaternion.identity, transform);
                    hitMarker.name = $"HitMarker_{note.time:F2}_{note.stringNumber}_{note.fretNumber}";
                    hitMarker.transform.localScale *= 1.2f;

                    Renderer hitMarkerRenderer = hitMarker.GetComponent<Renderer>();
                    if (hitMarkerRenderer != null && hitMarkerMaterials != null && note.stringNumber < hitMarkerMaterials.Length)
                    {
                        Material hitMatInstance = new Material(hitMarkerMaterials[note.stringNumber]);
                        hitMarkerRenderer.material = hitMatInstance;
                    }

                    hitMarker.AddComponent<HitMarkerFader>().fadeTime = hitMarkerFadeTime;
                    
                    Destroy(note.noteObject);
                    note.noteObject = null;
                    note.isSpawned = false;
                    note.hitMarkerSpawned = true;

                    if (note.fretLabelObject != null)
                    {
                        Destroy(note.fretLabelObject);
                        note.fretLabelObject = null;
                    }
                }
                else if (!devNotePositioningMode && timeSinceSpawn >= totalTravelTime)
                {
                    Destroy(note.noteObject);
                    note.noteObject = null;
                    note.isSpawned = false;

                    if (note.fretLabelObject != null)
                    {
                        Destroy(note.fretLabelObject);
                        note.fretLabelObject = null;
                    }
                }
                note.previousZPos = zPos;
            }
        }
    }

    public void UpdateHighwayPosition()
    {
        // This method is called by Main.cs when the user scrubs or jumps while paused.
        // It forces a single update pass to reposition all notes and markers.
        Update();
    }

    public void ResetHighway()
    {
        // Reset notes
        if (notes != null)
        {
            foreach (var note in notes)
            {
                if (note.noteObject != null) Destroy(note.noteObject);
                if (note.fretLabelObject != null) Destroy(note.fretLabelObject);
                note.isSpawned = false;
                note.hitMarkerSpawned = false;
                note.previousZPos = -1f;
            }
            notes = null; // Stop processing old notes
        }

        // Reset bar markers
        if (barMarkers != null)
        {
            foreach (var bar in barMarkers)
            {
                if (bar.markerObject != null) Destroy(bar.markerObject);
                if (bar.labelObject != null) Destroy(bar.labelObject);
                if (bar.perpendicularObject != null) Destroy(bar.perpendicularObject);
            }
            barMarkers.Clear(); // Clear the list
        }
    }


    private void CreateFretboardStrings()
    {
        if (stringMaterials == null || stringMaterials.Length == 0 || totalStrings <= 0)
        {
            Debug.LogWarning("Cannot create fretboard strings: stringMaterials is null/empty or totalStrings is zero.");
            return;
        }

        for (int i = 0; i < totalStrings; i++)
        {
            GameObject stringObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stringObject.name = $"FretboardString_{i}";
            stringObject.transform.SetParent(transform);

            float stringY = GetStringY(i) + fretboardYOffset;
            stringObject.transform.position = new Vector3(0f, stringY, hitZ);
            stringObject.transform.localScale = new Vector3(stringWidth, stringHeight, stringDepth);

            if (i < stringMaterials.Length)
            {
                Renderer renderer = stringObject.GetComponent<Renderer>();
                if (renderer != null) renderer.material = stringMaterials[i];
            }
            
            Collider collider = stringObject.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }
    }

    private void CreateFretboardVisuals()
    {
        float padding = 10.0f;
        float totalHeight = GetStringY(0) - GetStringY(totalStrings - 1) + stringHeight + padding;
        float verticalCenterY = GetStringY(totalStrings - 1) + (totalHeight / 2f) - (stringHeight / 2f) - (padding / 2f);

        for (int fret = 1; fret <= maxFretNumber; fret++)
        {
            GameObject fretLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fretLine.name = $"FretLine_{fret}";
            fretLine.transform.SetParent(transform);

            fretLine.transform.position = new Vector3(GetFretX(fret) - (fretSpacing / 2f), verticalCenterY, hitZ);
            fretLine.transform.localScale = new Vector3(fretLineThickness, totalHeight, fretLineDepth);

            Renderer renderer = fretLine.GetComponent<Renderer>();
            if (renderer != null && fretLineMaterial != null) renderer.material = fretLineMaterial;

            Collider collider = fretLine.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        if (inlayDotMaterial != null)
        {
            foreach (int fret in inlayFrets)
            {
                if (fret > 0 && fret <= maxFretNumber)
                {
                    int dotCount = (fret == 12) ? 2 : 1;
                    float fretCenterX = GetFretX(fret);

                    for (int i = 0; i < dotCount; i++)
                    {
                        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        dot.name = $"InlayDot_{fret}_{i}";
                        dot.transform.SetParent(transform);

                        float dotOffsetY = 0f;
                        if (dotCount == 2) dotOffsetY = (i == 0) ? doubleDotOffset : -doubleDotOffset;

                        dot.transform.position = new Vector3(fretCenterX, verticalCenterY + dotOffsetY, hitZ);
                        dot.transform.localScale = new Vector3(inlayDotScale, inlayDotScale, fretLineDepth * 2f);

                        Renderer renderer = dot.GetComponent<Renderer>();
                        if (renderer != null) renderer.material = inlayDotMaterial;

                        Collider collider = dot.GetComponent<Collider>();
                        if (collider != null) collider.enabled = false;
                    }
                }
            }
        }
    }
}