using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    
    private float inactivityTimer;
    private float inactivityThreshold = 60f; // 입력이 없는 경우 타이틀로 되돌아가는 시간
    
    private Settings jsonSetting;
    private CancellationTokenSource cToken;
    
    public readonly List<GameObject> addrInstances = new List<GameObject>();
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            cToken = new CancellationTokenSource();
            //DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private async void ClearAllDynamic()
    {
        for (int i = addrInstances.Count - 1; i >= 0; --i)
        {
            var go = addrInstances[i];
            if (go != null)
            {
                Addressables.ReleaseInstance(go); // Addressable 인스턴스 해제
            }
        }

        addrInstances.Clear(); // 목록 초기화
    }
}