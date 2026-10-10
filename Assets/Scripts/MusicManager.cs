using UnityEngine;

public class MusicManager : MonoBehaviour
{
    [Header("背景音乐")]
    public AudioClip backgroundMusic;

    [Header("音量")]
    [Range(0f, 1f)]
    public float volume = 0.4f;

    private AudioSource audioSource;

    void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = backgroundMusic;
        audioSource.loop = true;         // 循环播放
        audioSource.volume = volume;
        audioSource.playOnAwake = true;

        if (backgroundMusic != null)
            audioSource.Play();
    }
}