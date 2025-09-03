using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Video;

public class ButtonObject : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Hover scale settings")]
    [SerializeField] private float hoverScale = 1.1f;      // 마우스 오버 시 목표 스케일(절대)
    [SerializeField] private float duration = 0.12f;       // 트윈 시간
    [SerializeField] private AnimationCurve ease;   // 이징 커브
    
    private Vector3 baseScale;
    private Coroutine tween;
    private VideoPlayer videoPlayer;
    
    private void Awake()
    {
        if (ease == null) ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        baseScale = transform.localScale;   // 초기 스케일 복원용
        videoPlayer = GetComponent<VideoPlayer>();
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        ScaleTo(Vector3.one * hoverScale);
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        ScaleTo(Vector3.one); // 1.0으로 복귀
    }
    
    private void ScaleTo(Vector3 target)
    {
        if (tween != null) StopCoroutine(tween);
        tween = StartCoroutine(ScaleRoutine(target));
    }
    
    private IEnumerator ScaleRoutine(Vector3 target)
    {
        Vector3 start = transform.localScale;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            float e = ease.Evaluate(u);
            transform.localScale = Vector3.LerpUnclamped(start, target, e);
            yield return null;
        }

        transform.localScale = target;
        tween = null;
    }
    
    private void OnEnable()
    {
        if (videoPlayer != null && !string.IsNullOrEmpty(videoPlayer.url))
        {               
            StartCoroutine(VideoManager.Instance.RestartFromStart(videoPlayer));
        }
    }

    private void OnDisable()
    {
        if (tween != null) StopCoroutine(tween);
        transform.localScale = baseScale; // 비활성화 시 원래 스케일로 복원
    }
}
