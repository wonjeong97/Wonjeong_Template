using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    private CancellationTokenSource cts;

    // Addressables.InstantiateAsync로 만든 동적 오브젝트 추적
    private readonly List<GameObject> addrInstances = new List<GameObject>();

    private Settings jsonSetting;

    public Settings JsonSetting
    { 
        get => jsonSetting;
        private set => jsonSetting = value;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            //DontDestroyOnLoad(gameObject);

            cts = new CancellationTokenSource();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        if (JsonLoader.Instance.Settings == null)
        {
            Debug.LogError("[UIManager] Settings are not loaded yet.");
            return;
        }
        else
        {
            JsonSetting = JsonLoader.Instance.Settings;
            InitUI();
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        try { cts?.Cancel(); } catch { }
        cts?.Dispose();
        cts = null;

        // 생성했던 Addressables 인스턴스 정리
        for (int i = addrInstances.Count - 1; i >= 0; --i)
        {
            var go = addrInstances[i];
            if (go)
            {
                Addressables.ReleaseInstance(go);
            }
        }
        addrInstances.Clear();
    }

    private async void InitUI()
    {
        GameObject canvas = await CreateCanvasAsync();
        GameObject titleBG = await CreateBackgroundImageAsync(JsonSetting.testTitle.titleBackground, canvas, CancellationToken.None);
        if (titleBG)
        {
            titleBG.AddComponent<TitleManager>();
            if (titleBG.TryGetComponent<TitleManager>(out var manager))
            {
                manager.StartTitle(JsonSetting, titleBG);
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { }
    private void Update() { }

    #region Public API
    public Task CreatePopupAsync(PopupSetting setting, GameObject parent, UnityAction<GameObject> onClose = null, CancellationToken token = default)
        => CreatePopupInternalAsync(setting, parent, onClose, MergeToken(token));

    public Task<GameObject> CreatePageAsync(PageSetting page, GameObject parent, CancellationToken token = default)
        => CreatePageInternalAsync(page, parent, MergeToken(token));

    public void ClearAllDynamic() // 필요
    {
        for (int i = addrInstances.Count - 1; i >= 0; --i)
        {
            var go = addrInstances[i];
            if (go != null)
            {
                Addressables.ReleaseInstance(go);
            }
        }
        addrInstances.Clear();
    }

    public async Task<GameObject> CreateCanvasAsync(CancellationToken token = default)
    {
        var go = await InstantiateAsync("Prefabs/CanvasPrefab.prefab", null, MergeToken(token));
        if (!go) return null;

        // Canvas 초기 설정
        if (!go.TryGetComponent<Canvas>(out var canvas))
        {
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        // UI 렌더링을 위해 기본 UI 컴포넌트 추가
        if (!go.TryGetComponent<CanvasScaler>(out var scaler))
        {
            scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        if (!go.TryGetComponent<GraphicRaycaster>(out var raycaster))
        {
            raycaster = go.AddComponent<GraphicRaycaster>();
        }

        return go;
    }

    public async Task<GameObject> CreateBackgroundImageAsync(ImageSetting setting, GameObject parent, CancellationToken token)
    {
        var go = await InstantiateAsync("Prefabs/ImagePrefab.prefab", parent.transform, token);
        if (!go) return null;

        go.name = setting.name;

        if (go.TryGetComponent<Image>(out var image))
        {
            var texture = LoadTexture(setting.sourceImage);
            if (texture)
                image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            image.color = setting.color;
            image.type = (Image.Type)setting.type;
        }

        if (go.TryGetComponent<RectTransform>(out var rt))
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); // 좌상단
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(setting.rotation);
            rt.sizeDelta = setting.size;
        }

        return go;
    }

    private async Task<GameObject> CreateImageAsync(ImageSetting setting, GameObject parent, CancellationToken token)
    {
        var go = await InstantiateAsync("Prefabs/ImagePrefab.prefab", parent.transform, token);
        if (!go) return null;

        go.name = setting.name;

        if (go.TryGetComponent<Image>(out var image))
        {
            var texture = LoadTexture(setting.sourceImage);
            if (texture)
                image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            image.color = setting.color;
            image.type = (Image.Type)setting.type;
        }

        if (go.TryGetComponent<RectTransform>(out var rt))
        {
            rt.anchoredPosition = new Vector2(setting.position.x, -setting.position.y);
            rt.localRotation = Quaternion.Euler(setting.rotation);
            rt.sizeDelta = setting.size;
        }

        return go;
    }

    private async Task CreateTextsAsync(TextSetting[] settings, GameObject parent, CancellationToken token)
    {
        if (settings == null || settings.Length == 0) return;
        var tasks = new List<Task>(settings.Length);
        foreach (var setting in settings)
            tasks.Add(CreateSingleTextAsync(setting, parent, token));
        await Task.WhenAll(tasks);
    }

    private async Task CreateSingleTextAsync(TextSetting setting, GameObject parent, CancellationToken token)
    {
        var go = await InstantiateAsync("Prefabs/TextPrefab.prefab", parent.transform, token);
        if (!go) return;
        go.name = setting.name;

        if (go.TryGetComponent<TextMeshProUGUI>(out var uiText))
        {
            await LoadFontAndApplyAsync(uiText, setting.fontName, setting.text, setting.fontSize, setting.fontColor, setting.alignment, token);
        }
        if (go.TryGetComponent<RectTransform>(out var rt))
        {
            rt.anchoredPosition = new Vector2(setting.position.x, -setting.position.y);
            rt.localRotation = Quaternion.Euler(setting.rotation);
        }
    }

    // 버튼 생성기(사운드 연동 포함) – 경로 키("Assets/.../ButtonPrefab.prefab")도 그대로 사용 가능
    public async Task<(GameObject button, GameObject addImage)> CreateButtonAsync(ButtonSetting setting, GameObject parent, CancellationToken token)
    {
        // 현재 그룹 구성과 맞춰 경로 키 사용 – 라벨 키("ButtonPrefab")를 쓰고 싶다면 Addressables에 라벨을 추가하세요.
        var go = await InstantiateAsync("Prefabs/ButtonPrefab.prefab", parent.transform, token);
        if (!go) return (null, null);
        go.name = setting.name;

        // 배경 이미지
        if (go.TryGetComponent<Image>(out var bgImage) && setting.buttonBackgroundImage != null)
        {
            var tex = LoadTexture(setting.buttonBackgroundImage.sourceImage);
            if (tex)
                bgImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            bgImage.color = setting.buttonBackgroundImage.color;
            bgImage.type = (Image.Type)setting.buttonBackgroundImage.type;
        }

        // 텍스트
        var textComp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (textComp != null && setting.buttonText != null && !string.IsNullOrEmpty(setting.buttonText.text))
        {
            await LoadFontAndApplyAsync(textComp, setting.buttonText.fontName,
                                        setting.buttonText.text, setting.buttonText.fontSize, setting.buttonText.fontColor,
                                        TextAlignmentOptions.Center, token);

            if (textComp.TryGetComponent<RectTransform>(out var textRT))
            {
                textRT.anchoredPosition = new Vector2(setting.buttonText.position.x, setting.buttonText.position.y);
                textRT.localRotation = Quaternion.Euler(setting.buttonText.rotation);
            }
        }

        // 크기·위치
        if (go.TryGetComponent<RectTransform>(out var rt))
        {
            rt.sizeDelta = setting.size;
            rt.anchoredPosition = new Vector2(setting.position.x, -setting.position.y);
        }

        // 추가 이미지(옵션)
        GameObject addImgGO = null;
        if (setting.buttonAdditionalImage != null && !string.IsNullOrEmpty(setting.buttonAdditionalImage.sourceImage))
        {
            addImgGO = await CreateImageAsync(setting.buttonAdditionalImage, go, token);
            if (addImgGO && addImgGO.TryGetComponent<RectTransform>(out var addRT))
            {
                addRT.anchoredPosition = new Vector2(setting.buttonAdditionalImage.position.x, -setting.buttonAdditionalImage.position.y);
                addRT.sizeDelta = setting.buttonAdditionalImage.size;
            }
        }

        // 클릭 SFX(옵션)
        if (go.TryGetComponent<Button>(out var button))
        {
            var soundKey = setting.buttonSound;
            if (!string.IsNullOrEmpty(soundKey))
            {
                button.onClick.AddListener(() =>
                {
                    AudioManager.Instance?.Play(soundKey);
                });
            }
        }

        return (go, addImgGO);
    }

    private async Task CreatePopupInternalAsync(PopupSetting setting, GameObject parent, UnityAction<GameObject> onClose, CancellationToken token)
    {
        var popupBG = await CreateBackgroundImageAsync(setting.popupBackgroundImage, parent, token);
        if (!popupBG) return;
        popupBG.transform.SetAsLastSibling();

        var allTasks = new List<Task>(2)
        {
            CreateTextsAsync(setting.popupTexts, popupBG, token),
            CreatePopupImagesAsync(setting.popupImages, popupBG, token)
        };
        await Task.WhenAll(allTasks);

        var (btnGO, _) = await CreateButtonAsync(setting.popupButton, popupBG, token);
        if (btnGO && btnGO.TryGetComponent<Button>(out var btn))
        {
            btn.onClick.AddListener(() =>
            {
                SafeReleaseInstance(popupBG);
                onClose?.Invoke(popupBG);
            });
        }
    }

    private async Task CreatePopupImagesAsync(ImageSetting[] images, GameObject parent, CancellationToken token)
    {
        if (images == null || images.Length == 0) return;
        var tasks = new List<Task>(images.Length);
        foreach (var img in images)
            tasks.Add(CreateImageAsync(img, parent, token));
        await Task.WhenAll(tasks);
    }

    // 페이지 생성 골격: 병렬로 텍스트/이미지/키보드/비디오 생성
    private async Task<GameObject> CreatePageInternalAsync(PageSetting page, GameObject parent, CancellationToken token)
    {
        var pageRoot = new GameObject(string.IsNullOrEmpty(page.name) ? "GeneratedPage" : page.name);
        pageRoot.transform.SetParent(parent.transform, false);

        var jobs = new List<Task>(4);
        jobs.Add(CreateTextsAsync(page.texts, pageRoot, token));
        jobs.Add(CreatePopupImagesAsync(page.images, pageRoot, token));
        // TODO: CreateKeyboardsAsync(page.keyboards, pageRoot, token)
        // TODO: CreateVideosAsync(page.videos, pageRoot, token)
        await Task.WhenAll(jobs);

        return pageRoot;
    }
    #endregion

    #region Utilities (Addressables, Fonts, Materials, Files)
    private async Task<GameObject> InstantiateAsync(string key, Transform parent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var handle = Addressables.InstantiateAsync(key, parent);
        try
        {
            var go = await AwaitWithCancellation(handle, token);
            if (go != null) addrInstances.Add(go);
            return go;
        }
        catch (OperationCanceledException)
        {
            if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result)
                Addressables.ReleaseInstance(handle.Result);
            throw;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UIManager] Instantiate failed: {key}\n{e}");
            if (handle.IsValid() && handle.Result)
                Addressables.ReleaseInstance(handle.Result);
            return null;
        }
    }

    private async Task<T> LoadAssetAsync<T>(string key, CancellationToken token) where T : UnityEngine.Object
    {
        token.ThrowIfCancellationRequested();
        var handle = Addressables.LoadAssetAsync<T>(key);
        try { return await AwaitWithCancellation(handle, token); }
        finally { if (handle.IsValid()) Addressables.Release(handle); }
    }

    private static async Task<T> AwaitWithCancellation<T>(AsyncOperationHandle<T> handle, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (token.Register(() => tcs.TrySetResult(true)))
        {
            var completed = await Task.WhenAny(handle.Task, tcs.Task);
            if (completed == tcs.Task)
                throw new OperationCanceledException(token);
            return await handle.Task;
        }
    }

    private Texture2D LoadTexture(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
        if (!File.Exists(fullPath)) return null;
        byte[] fileData = File.ReadAllBytes(fullPath);
        var texture = new Texture2D(2, 2);
        texture.LoadImage(fileData);
        return texture;
    }

    private string ResolveFont(string key)
    {
        FontMaps fontMap = JsonLoader.Instance.Settings.fontMap;
        if (fontMap == null) return key;
        var field = typeof(FontMaps).GetField(key);
        if (field != null)
            return field.GetValue(fontMap) as string ?? key;
        return key;
    }

    private async Task<bool> LoadFontAndApplyAsync(TextMeshProUGUI uiText, string fontKey, string textValue, float fontSize,
        Color fontColor, TextAlignmentOptions alignment, CancellationToken token)
    {
        if (!uiText || string.IsNullOrEmpty(fontKey)) return false;

        uiText.enabled = false;
        string mappedFontName = ResolveFont(fontKey);

        try
        {
            var font = await LoadAssetAsync<TMP_FontAsset>(mappedFontName, token);
            token.ThrowIfCancellationRequested();
            uiText.font = font;
            uiText.fontSize = fontSize;
            uiText.color = fontColor;
            uiText.alignment = alignment;
            uiText.text = textValue;
            uiText.enabled = true;
            return true;
        }
        catch (OperationCanceledException) 
        {
            Debug.Log("load failed");
            return false; 
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UIManager] Font load failed: {mappedFontName}\n{e}");
            return false;
        }
    }

    private async Task<bool> LoadMaterialAndApplyAsync(Image targetImage, string materialKey, CancellationToken token)
    {
        if (!targetImage || string.IsNullOrEmpty(materialKey)) return false;
        try
        {
            var mat = await LoadAssetAsync<Material>(materialKey, token);
            token.ThrowIfCancellationRequested();
            targetImage.material = mat;
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception e)
        {
            Debug.LogWarning($"[UIManager] Material load failed: {materialKey}\n{e}");
            return false;
        }
    }

    private void SafeReleaseInstance(GameObject go)
    {
        if (!go) return;
        Addressables.ReleaseInstance(go);
        addrInstances.Remove(go);
    }

    private CancellationToken MergeToken(CancellationToken external)
    {
        if (cts == null) return external;
        if (!external.CanBeCanceled) return cts.Token;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, external);
        return linked.Token;
    }
    #endregion
}