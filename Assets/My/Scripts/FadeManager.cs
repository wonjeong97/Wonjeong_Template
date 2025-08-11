using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class FadeManager : MonoBehaviour
{
    public static FadeManager Instance { get; private set; }

    [SerializeField] private Image fadeImage;

    private void Awake()
    {
        if (Instance == null) { Instance = this; }
        else if (Instance != this) { Destroy(gameObject); return; }

        if (!fadeImage)
        {
            Debug.LogError("[FadeManager] Fade Image is not assigned.");
            return;
        }
        SetAlpha(0f);
    }

    public void FadeIn(float duration, Action onComplete = null)
    {
        fadeImage.raycastTarget = true;
        fadeImage.transform.SetAsLastSibling();
        StartCoroutine(Fade(1f, 0f, duration, false, () =>
        {
            fadeImage.raycastTarget = false;
            fadeImage.transform.SetAsFirstSibling();
            onComplete?.Invoke();
        }));
    }

    public void FadeOut(float duration, Action onComplete = null)
    {
        fadeImage.raycastTarget = true;
        fadeImage.transform.SetAsLastSibling();
        StartCoroutine(Fade(0f, 1f, duration, false, onComplete));
    }

    // 비동기 래퍼
    public Task FadeInAsync(float duration, bool unscaledTime = false)
        => RunFadeAsync(1f, 0f, duration, unscaledTime);

    public Task FadeOutAsync(float duration, bool unscaledTime = false)
        => RunFadeAsync(0f, 1f, duration, unscaledTime);

    private async Task RunFadeAsync(float from, float to, float duration, bool unscaled)
    {
        var tcs = new TaskCompletionSource<bool>();
        fadeImage.raycastTarget = true;
        fadeImage.transform.SetAsLastSibling();
        StartCoroutine(Fade(from, to, duration, unscaled, () => tcs.TrySetResult(true)));
        await tcs.Task;
        if (to <= 0.001f)
        {
            fadeImage.raycastTarget = false;
            fadeImage.transform.SetAsFirstSibling();
        }
    }

    private IEnumerator Fade(float from, float to, float duration, bool unscaled, Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float alpha = Mathf.Lerp(from, to, elapsed / duration);
            SetAlpha(alpha);
            elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }
        SetAlpha(to);
        onComplete?.Invoke();
    }

    private void SetAlpha(float alpha)
    {
        if (!fadeImage) return;
        var c = fadeImage.color;
        fadeImage.color = new Color(c.r, c.g, c.b, alpha);
    }
}