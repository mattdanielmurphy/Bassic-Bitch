using UnityEngine;

public class HitMarkerFader : MonoBehaviour
{
    public float fadeTime = 0.5f;
    public float shrinkMultiplier = 0.5f; // The final scale will be initialScale * shrinkMultiplier
    private float startTime;
    private Renderer noteRenderer;
    private Color startColor;
    private Vector3 startScale;

    void Start()
    {
        startTime = Time.time;
        noteRenderer = GetComponent<Renderer>();
        startScale = transform.localScale;

        if (noteRenderer != null)
        {
            // The material should already be a clone and the correct hit marker material
            // We just need to get its starting color for fading.
            startColor = noteRenderer.material.color;
        }
        else
        {
            Debug.LogWarning("HitMarkerFader requires a Renderer component on the GameObject.");
            Destroy(gameObject);
        }
    }

    void Update()
    {
        float t = (Time.time - startTime) / fadeTime;

        if (t >= 1.0f)
        {
            Destroy(gameObject);
            return;
        }

        // 1. Fade the alpha
        Color newColor = startColor;
        newColor.a = Mathf.Lerp(1f, 0f, t);
        noteRenderer.material.color = newColor;

        // 2. Shrink the scale (Y-axis only)
        float endScaleY = startScale.y * shrinkMultiplier;
        float currentScaleY = Mathf.Lerp(startScale.y, endScaleY, t);
        transform.localScale = new Vector3(startScale.x, currentScaleY, startScale.z);
    }
}