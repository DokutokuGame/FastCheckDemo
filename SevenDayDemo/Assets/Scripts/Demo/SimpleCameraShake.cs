using UnityEngine;

public sealed class SimpleCameraShake : MonoBehaviour
{
    public static SimpleCameraShake Instance { get; private set; }

    private Vector3 _basePos;
    private float _time;
    private float _duration;
    private float _amplitude;

    private void Awake()
    {
        Instance = this;
        _basePos = transform.localPosition;
    }

    public void Shake(float amplitude, float duration)
    {
        _amplitude = Mathf.Max(_amplitude, amplitude);
        _duration = Mathf.Max(_duration, duration);
        _time = 0f;
    }

    private void LateUpdate()
    {
        if (_duration <= 0f) return;

        _time += Time.deltaTime;
        float t = _time / _duration;
        if (t >= 1f)
        {
            _duration = 0f;
            _amplitude = 0f;
            transform.localPosition = _basePos;
            return;
        }

        // 简单噪声抖动（不追求优雅）
        float x = (Mathf.PerlinNoise(Time.time * 30f, 0f) - 0.5f) * 2f;
        float y = (Mathf.PerlinNoise(0f, Time.time * 30f) - 0.5f) * 2f;
        transform.localPosition = _basePos + new Vector3(x, y, 0) * _amplitude;
    }
}