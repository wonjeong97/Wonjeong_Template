using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class TitleManager : MonoBehaviour
{
    private void Start()
    {
    }

    public async void StartTitle(Settings settings, GameObject root)
    {
        if (settings == null)
        {
            Debug.Log("[TitleManager] settings is null");
            return;
        }            
        else
        {
            var (startBtn, _) = await UIManager.Instance.CreateButtonAsync(settings.testTitle.startButton, root, CancellationToken.None);
            if (startBtn && startBtn.TryGetComponent<Button>(out var btn))
            {
                btn.onClick.AddListener(() => 
                {
                    Debug.Log("start btn clicked");
                });
            }
        }
            
    }
}
