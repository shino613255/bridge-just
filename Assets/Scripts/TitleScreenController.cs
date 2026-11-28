using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>タイトル画面のSTARTボタンから本編のシーンへ移動する。</summary>
[DisallowMultipleComponent]
public sealed class TitleScreenController : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private string gameScenePath = "Assets/Scenes/Game Scene.unity";
    private bool starting;

    private void Awake() { if (startButton != null) startButton.onClick.AddListener(StartGame); }

    public void StartGame()
    {
        if (starting) return;
        starting = true;
        if (startButton != null) startButton.interactable = false;
        if (!Application.CanStreamedLevelBeLoaded(gameScenePath))
        {
            Debug.LogError("[BridgeJust] Game Scene is missing from the active Build Profile scene list.", this);
            starting = false;
            if (startButton != null) startButton.interactable = true;
            return;
        }
        SceneManager.LoadSceneAsync(gameScenePath, LoadSceneMode.Single);
    }

    private void OnDestroy() { if (startButton != null) startButton.onClick.RemoveListener(StartGame); }
}
