using UnityEngine;
using DG.Tweening;

public class FakeDragIntroLoop : MonoBehaviour
{
    public static FakeDragIntroLoop Instance { get; private set; }

    public BoardController board;
    public float firstDelay = 0.8f;
    public float repeatInterval = 2.0f;

    public float liftScale = 1.15f;
    public float liftTime = 0.12f;
    public float dragTime = 0.35f;
    public float returnTime = 0.25f;

    bool _stopped;

    // 关键：记住“演示开始前”的状态
    Transform _active;
    Vector3 _startPos;
    Vector3 _startScale;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        ScheduleNext(firstDelay);
    }

    void Update()
    {
        if (_stopped) return;

        if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
        {
            StopForeverAndRestore(); // 点任何地方都停并恢复
        }
    }

    public void CancelIfAnimating(Transform t)
    {
        if (_stopped) return;
        if (_active != t) return;

        StopForeverAndRestore();
    }

    void StopForeverAndRestore()
    {
        _stopped = true;
        DOTween.Kill(this);

        if (_active != null)
        {
            DOTween.Kill(_active);

            // 恢复位置/缩放
            _active.position = _startPos;
            _active.localScale = _startScale;

            // 恢复格子占用（避免 OnMouseDown 已 ClearCell）
            if (board != null && board.introHintModule != null)
            {
                var cell = board.introHintModule.CurrentCell;
                if (cell.x >= 0 && cell.y >= 0 && board.IsCellEmpty(cell))
                    board.PlaceModule(board.introHintModule, cell);
            }

            _active = null;
        }
    }

    void ScheduleNext(float delay)
    {
        DOTween.Sequence()
            .SetId(this)
            .AppendInterval(delay)
            .AppendCallback(() =>
            {
                if (_stopped) return;
                if (board == null || board.introHintModule == null) return;

                PlayOnce(board.introHintModule.transform);
                ScheduleNext(repeatInterval);
            });
    }

    void PlayOnce(Transform t)
    {
        DOTween.Kill(t);

        _active = t;
        _startPos = t.position;
        _startScale = t.localScale;

        Vector3 targetPos = board.CellToWorld(board.introTargetCell);
        Vector3 toward = (targetPos - _startPos).normalized * (board.cellSize * 2 * 0.8f);
        Vector3 endPos = _startPos + toward;

        Sequence seq = DOTween.Sequence().SetTarget(t).SetId(t);
        seq.Append(t.DOScale(_startScale * liftScale, liftTime).SetEase(Ease.OutQuad));
        seq.Append(t.DOMove(endPos, dragTime).SetEase(Ease.OutQuad));
        seq.Append(t.DOMove(_startPos, returnTime).SetEase(Ease.InQuad));
        seq.Append(t.DOScale(_startScale, liftTime).SetEase(Ease.OutQuad));
    }
}
