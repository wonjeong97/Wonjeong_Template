using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Video;

[DisallowMultipleComponent]
public class PopupObject : MonoBehaviour
{
    private bool isDestroying;

    private void OnDisable()
    {
        if (isDestroying) return;
        isDestroying = true;

        List<Transform> children = new List<Transform>();
        foreach (Transform t in transform) children.Add(t);

        UIManager ui = UIManager.Instance;
        
        foreach (Transform t in children)
        {
            GameObject go = t.gameObject;

            // VideoPlayer가 만든 RenderTexture 정리
            VideoPlayer[] vps = go.GetComponentsInChildren<VideoPlayer>(true);
            foreach (VideoPlayer vp in vps)
            {
                RenderTexture rt = vp.targetTexture;
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                    vp.targetTexture = null;
                }
            }

            // Addressables로 생성된 것만 해제, 아니면 일반 Destroy
            if (ui != null && ui.addrInstances.Remove(go))
            {
                Addressables.ReleaseInstance(go);
            }
            else
            {
                Destroy(go);
            }
        }

        Destroy(gameObject);
    }
    
}
