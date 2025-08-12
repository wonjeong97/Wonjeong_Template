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

    private async void Start()
    {
        if (JsonLoader.Instance.Settings == null)
        {
            Debug.LogError("[UIManager] Settings are not loaded yet.");
            return;
        }
        else
        {
            JsonSetting = JsonLoader.Instance.Settings;

            try
            {
                await InitUI();
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[UIManager] UI initialization canceled.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIManager] UI initialization failed: {e}");
            }
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

    private async Task InitUI(CancellationToken token = default)
    {
        var ct = MergeToken(token);

        GameObject canvas = null;
        GameObject titleBG = null;

        try
        {
            // (옵션) 페이드 아웃 후 생성, 끝나면 페이드 인
            // if (FadeManager.Instance) await FadeManager.Instance.FadeOutAsync(0.2f);

            // 1) 캔버스 생성
            canvas = await CreateCanvasAsync(ct);
            if (!canvas) throw new Exception("[InitUI] Canvas creation failed.");

            // 2) 타이틀 배경 생성
            var bgSetting = JsonSetting.testTitle.titleBackground; // 기존 코드 유지
            titleBG = await CreateBackgroundImageAsync(bgSetting, canvas, ct);
            if (titleBG)
            {
                titleBG.AddComponent<PageBook>();
                titleBG.AddComponent<PageSwipe>();
            }
            else
            {
                throw new Exception("[InitUI] Title background creation failed.");
            }

            // 3) 타이틀 매니저 부착 및 시작
            var manager = titleBG.GetComponent<TitleManager>() ?? titleBG.AddComponent<TitleManager>();
            manager.StartTitle(JsonSetting, titleBG);

            // (옵션) 생성 완료 후 페이드 인
            // if (FadeManager.Instance) await FadeManager.Instance.FadeInAsync(0.2f);
        }
        catch (OperationCanceledException)
        {
            // 취소 시 부분 생성된 것 정리
            if (titleBG) SafeReleaseInstance(titleBG);
            if (canvas) SafeReleaseInstance(canvas);
            throw; // 호출자에게 취소 전달
        }
        catch (Exception e)
        {
            Debug.LogError($"[InitUI] Failed.\n{e}");
            // 실패 시 부분 생성 정리
            if (titleBG) SafeReleaseInstance(titleBG);
            if (canvas) SafeReleaseInstance(canvas);
            throw;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { }
    private void Update() { }

    #region Public API
    public Task CreatePopupAsync(PopupSetting setting, GameObject parent, UnityAction<GameObject> onClose = null, CancellationToken token = default)
        => CreatePopupInternalAsync(setting, parent, onClose, MergeToken(token));

    public Task<GameObject> CreatePageAsync(PageSetting page, GameObject parent, CancellationToken token = default)
        => CreatePageInternalAsync(page, parent, MergeToken(token));

    /// <summary>
    /// Addressables.InstantiateAsync로 동적으로 생성된 모든 GameObject를 해제하고 목록을 비움
    /// </summary>
    public void ClearAllDynamic() // 필요
    {
        for (int i = addrInstances.Count - 1; i >= 0; --i)
        {
            var go = addrInstances[i];
            if (go != null)
            {
                Addressables.ReleaseInstance(go); // Addressables 인스턴스 해제
            }
        }
        addrInstances.Clear(); // 목록 초기화
    }

    /// <summary>
    /// Canvas 프리팹을 비동기로 생성하고 기본 UI 구성요소(Canvas, CanvasScaler, GraphicRaycaster)를 보장
    /// </summary>
    /// <param name="token">작업 도중 취소할 수 있는 CancellationToken</param>
    /// <returns></returns>
    public async Task<GameObject> CreateCanvasAsync(CancellationToken token = default)
    {
        // 1. Canvas 프리팹 인스턴스 생성
        var go = await InstantiateAsync("Prefabs/CanvasPrefab.prefab", null, MergeToken(token));
        if (!go) return null;

        // 2. Canvas 컴포넌트 보장
        if (!go.TryGetComponent<Canvas>(out var canvas))
        {
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; // 화면 오버레이 모드
        }

        // 3. CanvasScaler 컴포넌트 보장
        if (!go.TryGetComponent<CanvasScaler>(out var scaler))
        {
            scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // 해상도 기준
        }

        // 4. GraphicRaycaster 컴포넌트 보장 (UI 클릭/터치 감지)
        if (!go.TryGetComponent<GraphicRaycaster>(out var raycaster))
        {
            raycaster = go.AddComponent<GraphicRaycaster>();
        }

        return go;
    }

    /// <summary>
    /// 배경용 이미지를 부모 아래에 비동기로 생성하고,
    /// 캔버스 좌상단(0,1) 기준으로 고정 배치
    /// </summary>
    /// <param name="setting">배경 이미지 설정</param>
    /// <param name="parent">생성된 배경 이미지의 부모 GameObject</param>
    /// <param name="token">작업 도중 취소를 위한 CancellationToken</param>
    /// <returns></returns>
    public async Task<GameObject> CreateBackgroundImageAsync(ImageSetting setting, GameObject parent, CancellationToken token)
    {
        // 1. Addressables로 Image 프리팹을 비동기 인스턴스화
        var go = await InstantiateAsync("Prefabs/ImagePrefab.prefab", parent.transform, token);
        if (!go) return null;

        go.name = setting.name;

        // 2. Image 컴포넌트가 존재하면 스프라이트/색/타입 적용
        if (go.TryGetComponent<Image>(out var image))
        {
            // StreamingAssets에서 Texture2D 로드 후 Sprite 생성
            var texture = LoadTexture(setting.sourceImage);
            if (texture)
                image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            image.color = setting.color;
            image.type = (Image.Type)setting.type;
        }

        // 3. RectTransform 설정
        //    - anchorMin/anchorMax/pivot을 모두 (0,1)로 맞춰 좌상단 기준 고정 배치
        //    - anchoredPosition을 (0,0)으로 두어 부모 좌상단에 정확히 붙임
        //    - 회전/크기는 설정값 적용
        if (go.TryGetComponent<RectTransform>(out var rt))
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); // 좌상단 고정
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(setting.rotation);
            rt.sizeDelta = setting.size;
        }

        return go;
    }

    /// <summary>
    /// ImageSetting 데이터를 기반으로 부모 객체 아래에 이미지를 비동기로 생성
    /// </summary>
    /// <param name="setting">생성할 이미지 설정 데이터</param>
    /// <param name="parent">생성된 이미지의 부모 GameObject</param>
    /// <param name="token">작업 도중 최소를 위한 토큰</param>
    /// <returns></returns>
    private async Task<GameObject> CreateImageAsync(ImageSetting setting, GameObject parent, CancellationToken token)
    {
        // 1. Addressables를 통해 ImagePrefab 인스턴스 생성
        var go = await InstantiateAsync("Prefabs/ImagePrefab.prefab", parent.transform, token);
        if (!go) return null;

        go.name = setting.name;

        // 2. Image 컴포넌트가 있으면 텍스처 로드 후 속성 적용
        if (go.TryGetComponent<Image>(out var image))
        {
            var texture = LoadTexture(setting.sourceImage);

            if (texture) // Texture2D를 Sprite로 변환해 적용
                image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            image.color = setting.color;
            image.type = (Image.Type)setting.type;
        }

        // 3. RectTransform 속성 적용
        if (go.TryGetComponent<RectTransform>(out var rt))
        {
            rt.anchoredPosition = new Vector2(setting.position.x, -setting.position.y);
            rt.localRotation = Quaternion.Euler(setting.rotation);
            rt.sizeDelta = setting.size;
        }

        return go;
    }

    /// <summary>
    /// 여러 TextSetting을 받아 부모 아래에 비동기로 텍스트들을 생성
    /// </summary>
    /// <param name="settings">생성할 텍스트 설정 배열</param>
    /// <param name="parent">생성된 텍스트의 부모 GameObject</param>
    /// <param name="token">작업 도중 취소를 위한 토큰</param>
    /// <returns></returns>
    private async Task CreateTextsAsync(TextSetting[] settings, GameObject parent, CancellationToken token)
    {
        // 설정이 없으면 아무 것도 하지 않음
        if (settings == null || settings.Length == 0) return;

        // 병렬 실행할 작업 수에 맞게 리스트 초기 용량 예약
        var tasks = new List<Task>(settings.Length);

        // 각 텍스트 생성 작업 추가
        foreach (var setting in settings)
            tasks.Add(CreateSingleTextAsync(setting, parent, token));

        // 모든 텍스트 생성 완료 대기
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 단일 TextSetting을 기반으로 TextPrefab을 인스턴스화하고 속성을 적용
    /// </summary>
    /// <param name="setting">단일 텍스트 생성/스타일 정보</param>
    /// <param name="parent">생성된 텍스트의 부모 GameObject</param>
    /// <param name="token">작업 도중 취소를 위한 토큰</param>
    /// <returns></returns>
    private async Task CreateSingleTextAsync(TextSetting setting, GameObject parent, CancellationToken token)
    {
        // Text 프리팹 생성 (Addressables)
        var go = await InstantiateAsync("Prefabs/TextPrefab.prefab", parent.transform, token);
        if (!go) return;

        go.name = setting.name;

        // 텍스트 시각 속성 적용
        if (go.TryGetComponent<TextMeshProUGUI>(out var uiText))
        {
            // 폰트는 Addressables에서 로드되며, alignment와 텍스트 등도 함께 적용
            await LoadFontAndApplyAsync(
                uiText,
                setting.fontName,
                setting.text,
                setting.fontSize,
                setting.fontColor,
                setting.alignment,
                token
                );
        }

        // 위치/회전 적용
        if (go.TryGetComponent<RectTransform>(out var rt))
        {
            rt.anchoredPosition = new Vector2(setting.position.x, -setting.position.y);
            rt.localRotation = Quaternion.Euler(setting.rotation);
        }
    }

    /// <summary>
    /// ButtonSetting 정보를 기반으로 버튼 UI를 동적으로 생성하고 설정합니다.
    /// </summary>
    /// <param name="setting">버튼의 배경, 텍스트, 추가 이미지, 사운드 등의 설정 데이터</param>
    /// <param name="parent">생성한 버튼을 부착할 부모 GameObject</param>
    /// <param name="token">작업 도중 취소할 수 있는 CancellationToken</param>
    /// <returns></returns>
    public async Task<(GameObject button, GameObject addImage)> CreateButtonAsync(ButtonSetting setting, GameObject parent, CancellationToken token)
    {
        // 1. 버튼 프리팹 인스턴스 생성
        var go = await InstantiateAsync("Prefabs/ButtonPrefab.prefab", parent.transform, token);
        if (!go) return (null, null);

        go.name = setting.name;

        // 2. 배경 이미지 설정
        if (go.TryGetComponent<Image>(out var bgImage) && setting.buttonBackgroundImage != null)
        {
            var tex = LoadTexture(setting.buttonBackgroundImage.sourceImage);
            if (tex)
                bgImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            bgImage.color = setting.buttonBackgroundImage.color;
            bgImage.type = (Image.Type)setting.buttonBackgroundImage.type;
        }

        // 3. 버튼 텍스트 설정
        var textComp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (textComp != null && setting.buttonText != null && !string.IsNullOrEmpty(setting.buttonText.text))
        {
            // 폰트 및 텍스트 속성 적용
            await LoadFontAndApplyAsync(
                textComp,
                setting.buttonText.fontName,
                setting.buttonText.text,
                setting.buttonText.fontSize,
                setting.buttonText.fontColor,
                TextAlignmentOptions.Center, token
                );

            // 텍스트 위치/회전 적용
            if (textComp.TryGetComponent<RectTransform>(out var textRT))
            {
                textRT.anchoredPosition = new Vector2(setting.buttonText.position.x, setting.buttonText.position.y);
                textRT.localRotation = Quaternion.Euler(setting.buttonText.rotation);
            }
        }

        // 4. 버튼 크기 및 위치 설정
        if (go.TryGetComponent<RectTransform>(out var rt))
        {
            rt.sizeDelta = setting.size;
            rt.anchoredPosition = new Vector2(setting.position.x, -setting.position.y);
        }

        // 5. 추가 이미지 생성(옵션)
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

        // 6. 버튼 클릭 사운드 설정(옵션)
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

        // 7. 버튼과 추가 이미지 반환
        return (go, addImgGO);
    }

    /// <summary>
    /// PopupSetting 데이터를 기반으로 팝업 UI를 동적으로 생성
    /// </summary>
    /// <param name="setting">팝업의 배경, 텍스트, 이미지, 버튼 등에 대한 설정 데이터</param>
    /// <param name="parent">팝업을 부착할 부모 GameObject</param>
    /// <param name="onClose">팝업이 닫힐 때 호출할 콜백</param>
    /// <param name="token">작업 도중 취소할 수 있는 CancellationToken</param>
    /// <returns></returns>
    private async Task CreatePopupInternalAsync(PopupSetting setting, GameObject parent, UnityAction<GameObject> onClose, CancellationToken token)
    {
        // 1. 팝업 배경 생성
        var popupBG = await CreateBackgroundImageAsync(setting.popupBackgroundImage, parent, token);
        if (!popupBG) return;

        // 다른 UI 위에 표시되도록 맨 뒤에서 앞으로 이동
        popupBG.transform.SetAsLastSibling();

        // 2. 텍스트와 이미지 생성 작업을 병렬 실행
        var allTasks = new List<Task>(2)
        {
            CreateTextsAsync(setting.popupTexts, popupBG, token),       // 팝업 내부 텍스트 생성
            CreatePopupImagesAsync(setting.popupImages, popupBG, token) // 팝업 내부 이미지 생성
        };
        await Task.WhenAll(allTasks);

        // 3. 닫기 버튼 생성
        var (btnGO, _) = await CreateButtonAsync(setting.popupButton, popupBG, token);
        if (btnGO && btnGO.TryGetComponent<Button>(out var btn))
        {
            // 버튼 클릭 시 팝업 제거 + 콜백 호출
            btn.onClick.AddListener(() =>
            {
                SafeReleaseInstance(popupBG);    // Addressables 인스턴스 해제
                onClose?.Invoke(popupBG);        // 외부 콜백 실행
            });
        }
    }

    /// <summary>
    /// 팝업에 사용할 이미지들을 비동기로 생성
    /// </summary>
    /// <param name="images">생성할 이미지 설정 배열 (ImageSetting[])</param>
    /// <param name="parent">생성된 이미지들을 붙일 부모 GameObject</param>
    /// <param name="token">작업 도중 취소할 수 있는 CancellationToken</param>
    /// <returns></returns>
    private async Task CreatePopupImagesAsync(ImageSetting[] images, GameObject parent, CancellationToken token)
    {
        // 1. 유효한 데이터가 없으면 종료
        if (images == null || images.Length == 0) return;

        // 2. 병렬 실행할 작업 리스트 준비
        var tasks = new List<Task>(images.Length);

        // 3. 각 이미지 생성 작업 추가
        foreach (var img in images)
            tasks.Add(CreateImageAsync(img, parent, token));

        // 4. 모든 이미지 생성이 완료될 때까지 대기
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// PageSetting 정보를 기반으로 하나의 페이지를 동적으로 생성합니다.
    /// </summary>
    /// <param name="page">페이지에 포함될 텍스트, 이미지, 키보드, 비디오 등의 설정 데이터</param>
    /// <param name="parent">생성한 페이지를 붙일 부모 GameObject</param>
    /// <param name="token">취소 토큰 (작업 도중 취소 가능)</param>
    /// <returns></returns>
    private async Task<GameObject> CreatePageInternalAsync(PageSetting page, GameObject parent, CancellationToken token)
    {
        // 1. 페이지 루트 오브젝트 생성
        var pageRoot = new GameObject(string.IsNullOrEmpty(page.name) ? "GeneratedPage" : page.name);

        // 2. 부모 지정 (worldPositionStays=false로 로컬 위치 유지)        
        pageRoot.transform.SetParent(parent.transform, false);
        var rt = pageRoot.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(page.position.x, -page.position.y);
        rt.sizeDelta = page.size;

        pageRoot.AddComponent<CanvasGroup>();

        // 3. 병렬 실행할 작업 목록 준비
        var jobs = new List<Task>(4);
        jobs.Add(CreateTextsAsync(page.texts, pageRoot, token));        // 텍스트 생성
        jobs.Add(CreatePopupImagesAsync(page.images, pageRoot, token)); // 이미지 생성
        // TODO: CreateKeyboardsAsync(page.keyboards, pageRoot, token)  // 키보드 UI 생성
        // TODO: CreateVideosAsync(page.videos, pageRoot, token)        // 비디오 UI 생성

        // 4. 모든 생성 작업 완료 대기
        await Task.WhenAll(jobs);

        // 5. 생성된 페이지 루트 반환
        return pageRoot;
    }
    #endregion

    #region Utilities (Addressables, Fonts, Materials, Files)
    /// <summary>
    ///  Addressables를 사용해 비동기로 프리팹을 인스턴스화(Instantiate)
    /// </summary>
    /// <param name="key">Addressables에서 로드할 프리팹 키</param>
    /// <param name="parent">생성할 오브젝트의 부모 Transform</param>
    /// <param name="token">생성 작업을 취소할 수 있는 CancellationToken</param>
    /// <returns>생성된 GameObject, 실패 시 null</returns>
    private async Task<GameObject> InstantiateAsync(string key, Transform parent, CancellationToken token)
    {
        // 호출 즉시 취소 요청이 있었는지 확인함
        token.ThrowIfCancellationRequested();

        // Addressable로 비동기 인스턴스 생성 시작
        var handle = Addressables.InstantiateAsync(key, parent);
        try
        {
            // 취소 토큰을 고려한 Await
            var go = await AwaitWithCancellation(handle, token);

            // 정상 생성시 추적 리스트에 추가
            if (go != null) addrInstances.Add(go);
            return go;
        }
        catch (OperationCanceledException)  // 취소된 경우
        {
            // 생성이 완료된 상태라면 즉시 해제하여 메모리 / 참조 누수 방지
            if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result)
                Addressables.ReleaseInstance(handle.Result);
            throw;
        }
        catch (Exception e) // 생성 중 예외 발생 시
        {
            Debug.LogWarning($"[UIManager] Instantiate failed: {key}\n{e}");

            // 생성된 경우 즉시 해제
            if (handle.IsValid() && handle.Result)
                Addressables.ReleaseInstance(handle.Result);
            return null;
        }
    }

    /// <summary>
    /// Addressables를 사용하여 비동기로 에셋을 로드
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="key"> Addressables에서 로드할 에셋 키</param>
    /// <param name="token">로드 작업을 취소할 수 있는 CancellationToken</param>
    /// <returns>로드된 에셋(T 타입), 취소 시 OperationCanceledException 발생</returns>
    private async Task<T> LoadAssetAsync<T>(string key, CancellationToken token) where T : UnityEngine.Object
    {
        // 호출 시 즉시 취소 요청 확인
        token.ThrowIfCancellationRequested();

        // Addressables로 비동기 에셋 로
        var handle = Addressables.LoadAssetAsync<T>(key);

        try
        {
            // 취소 가능 대기
            return await AwaitWithCancellation(handle, token);
        }
        finally
        {   // 로드 완료 혹은 취소 시 handle 해제
            if (handle.IsValid())
                Addressables.Release(handle);
        }
    }

    /// <summary>
    /// Addressables의 AsyncOperationHandle을 await하면서 CancellationToken 지원
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="handle">Addressables 비동기 로드/생성 핸들</param>
    /// <param name="token">취소 시 OperationCanceledException 발생</param>
    /// <returns>정상 완료 시 handle.Task의 결과 반환</returns>
    /// <exception cref="OperationCanceledException"></exception>
    private static async Task<T> AwaitWithCancellation<T>(AsyncOperationHandle<T> handle, CancellationToken token)
    {
        // 취소 신호를 감지할 Task 생성
        var tcs = new TaskCompletionSource<bool>();

        // 토큰이 취소되면 tcs를 완료시켜 Task.WhenAny에서 취소를 먼저 감지할 수 있도록 함
        using (token.Register(() => tcs.TrySetResult(true)))
        {
            // 로드 완료(handle.Task)와 취소(tcs.Task) 중 하나가 먼저 끝날 때까지 대기
            var completed = await Task.WhenAny(handle.Task, tcs.Task);

            // 취소 Task가 먼저 끝난 경우
            if (completed == tcs.Task)
                throw new OperationCanceledException(token);

            // 정상적으로 handle.Task가 끝난 경우 결과 반환
            return await handle.Task;
        }
    }

    /// <summary>
    /// StreamingAssets 경로에서 Texture2D를 로드
    /// </summary>
    /// <param name="relativePath">StreamingAssets 하위의 상대 경로</param>
    /// <returns>로드된 Texture2D 객체, 실패 시 null</returns>
    private Texture2D LoadTexture(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        // StreamingAssets 폴더 기준 전체 경로 생성
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

        // 파일 존재 여부 확인
        if (!File.Exists(fullPath)) return null;

        // 바이너리로 읽어서 Texture2D 생성
        byte[] fileData = File.ReadAllBytes(fullPath);
        var texture = new Texture2D(2, 2); // 임시 크기, LoadImage 시 자동 변경
        texture.LoadImage(fileData);

        return texture;
    }

    /// <summary>
    /// 폰트 키를 실제 Addressables 리소스 키로 변환
    /// </summary>
    /// <param name="key">JSON 설정에서 지정한 폰트 맵핑 키</param>
    /// <returns> 매핑된 폰트 이름(또는 원본 키)</returns>
    private string ResolveFont(string key)
    {
        FontMaps fontMap = JsonLoader.Instance.Settings.fontMap;

        // fontMap이 없으면 변환 없이 반환
        if (fontMap == null) return key;

        // fontMap에서 해당 키에 해당하는 필드 찾기
        var field = typeof(FontMaps).GetField(key);
        if (field != null)
            return field.GetValue(fontMap) as string ?? key;
        return key;
    }

    /// <summary>
    /// Addressables에서 폰트를 비동기로 로드하여 지정한 TextMeshProUGUI에 적용
    /// </summary>
    /// <param name="uiText">폰트를 적용할 TextMeshProUGUI 컴포넌트</param>
    /// <param name="fontKey">JSON 설정에서 정의된 폰트 키</param>
    /// <param name="textValue">적용할 문자열</param>
    /// <param name="fontSize">글자 크기</param>
    /// <param name="fontColor">글자 색상</param>
    /// <param name="alignment">텍스트 정렬 방식</param>
    /// <param name="token">취소 토큰 (작업 중단 가능)</param>
    /// <returns>성공 여부 (true: 적용 성공, false: 실패 또는 취소)</returns>
    private async Task<bool> LoadFontAndApplyAsync(TextMeshProUGUI uiText, string fontKey, string textValue, float fontSize,
        Color fontColor, TextAlignmentOptions alignment, CancellationToken token)
    {
        // UI 텍스트 객체나 폰트 키가 없으면 실패 처리
        if (!uiText || string.IsNullOrEmpty(fontKey)) return false;

        // 로드 중 깜박임 방지를 위해 비활성화
        uiText.enabled = false;

        // fontKey를 fontMap에서 실제 리소스 키로 변환
        string mappedFontName = ResolveFont(fontKey);

        try
        {
            // Addressables에서 TMP_FontAsset 로드
            var font = await LoadAssetAsync<TMP_FontAsset>(mappedFontName, token);

            // 로드 도중 취소되었는지 최종 확인
            token.ThrowIfCancellationRequested();

            // 폰트 속성 적용
            uiText.font = font;
            uiText.fontSize = fontSize;
            uiText.color = fontColor;
            uiText.alignment = alignment;
            uiText.text = textValue;

            // 적용 후 활성화
            uiText.enabled = true;

            return true;
        }
        catch (OperationCanceledException)
        {
            // 취소된 경우 로그 출력 후 false 반환
            Debug.Log("load failed");
            return false;
        }
        catch (Exception e)
        {
            // 로드 실패 예외 로그
            Debug.LogWarning($"[UIManager] Font load failed: {mappedFontName}\n{e}");
            return false;
        }
    }

    /// <summary>
    /// Addressables에서 Material을 비동기로 로드하여 지정한 Image 컴포넌트에 적용
    /// </summary>
    /// <param name="targetImage">머티리얼을 적용할 UI Image 컴포넌트</param>
    /// <param name="materialKey">Addressables에 등록된 머티리얼 키</param>
    /// <param name="token">취소 토큰 (작업 도중 취소 가능)</param>
    /// <returns>성공 여부 (true: 적용 성공, false: 실패 또는 취소)</returns>
    private async Task<bool> LoadMaterialAndApplyAsync(Image targetImage, string materialKey, CancellationToken token)
    {
        // 대상 이미지 또는 키가 없으면 바로 실패 처리
        if (!targetImage || string.IsNullOrEmpty(materialKey)) return false;

        try
        {
            // Addressables에서 Material 로드
            var mat = await LoadAssetAsync<Material>(materialKey, token);

            // 로드 도중 취소 요청이 있었는지 최종 확인
            token.ThrowIfCancellationRequested();

            // 머티리얼을 이미지에 적용
            targetImage.material = mat;
            return true;
        }
        catch (OperationCanceledException)
        {
            // 취소된 경우 false 반환
            return false;
        }
        catch (Exception e)
        {
            // 로드 실패 시 경고 출력
            Debug.LogWarning($"[UIManager] Material load failed: {materialKey}\n{e}");
            return false;
        }
    }

    /// <summary>
    ///  Addressables로 생성된 인스턴스를 안전하게 해제
    /// </summary>
    /// <param name="go">해제할 GameObject 인스턴스</param>
    private void SafeReleaseInstance(GameObject go)
    {
        if (!go) return; // 유효하지 않으면 종료
        Addressables.ReleaseInstance(go);    // Addressables 인스턴스 해제
        addrInstances.Remove(go);   // 추적 리스트에서 제거
    }

    /// <summary>
    /// 내부 CancellationToken(cts.Token)과 외부 토큰을 병합하여 단일 토큰 반환.
    /// </summary>
    /// <param name="external">외부에서 전달된 CancellationToken</param>
    /// <returns>병합된 CancellationToken</returns>
    private CancellationToken MergeToken(CancellationToken external)
    {
        if (cts == null) return external; // 내부 토큰이 없으면 외부 토큰 그대로 사용

        if (!external.CanBeCanceled)
            return cts.Token; // 외부 토큰이 취소 불가능하면 내부 토큰만 사용

        // 두 토큰을 병합하여 하나의 LinkedToken 생성
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, external);

        return linked.Token;
    }
    #endregion
}