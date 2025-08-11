using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private readonly Dictionary<string, AudioClip> soundMap = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, float> soundVolumeMap = new Dictionary<string, float>();
    private AudioSource sfxSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            //DontDestroyOnLoad(gameObject);
            sfxSource = gameObject.GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        StartCoroutine(LoadSoundsFromSettings());
    }

    /// <summary>
    /// JSON(Settings.sounds) 기준으로 StreamingAssets/Audio에서 사운드 사전 로딩.
    /// WAV/OGG/MP3 확장자 자동 판별.
    /// </summary>
    private IEnumerator LoadSoundsFromSettings()
    {
        soundMap.Clear();
        soundVolumeMap.Clear();

        var settings = JsonLoader.Instance.Settings;
        if (settings == null || settings.sounds == null) yield break;

        foreach (var entry in settings.sounds)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, "Audio", entry.clipPath).Replace("\\", "/");
            string url = "file://" + fullPath;
            string ext = Path.GetExtension(fullPath).ToLower();

            AudioType type = AudioType.WAV;
            if (ext == ".ogg") type = AudioType.OGGVORBIS;
            else if (ext == ".mp3") type = AudioType.MPEG;

            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                yield return www.SendWebRequest();
                if (www.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(www);
                    clip.name = entry.key;
                    soundMap[entry.key] = clip;
                    soundVolumeMap[entry.key] = entry.volume;
                }
                else
                {
                    Debug.LogWarning($"[AudioManager] Load failed: {entry.clipPath} - {www.error}");
                }
            }
        }
    }

    /// <summary>
    /// 간단 재생 API: 키로 등록된 클립을 PlayOneShot.
    /// </summary>
    public bool Play(string key, float? volumeOverride = null)
    {
        if (!sfxSource) return false;
        if (!soundMap.TryGetValue(key, out var clip) || clip == null)
        {
            Debug.LogWarning($"[AudioManager] Key not found: {key}");
            return false;
        }
        float vol = volumeOverride ?? (soundVolumeMap.TryGetValue(key, out var v) ? v : 1f);
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(vol));
        return true;
    }

    public void StopAll()
    {
        if (sfxSource && sfxSource.isPlaying) sfxSource.Stop();
    }
}