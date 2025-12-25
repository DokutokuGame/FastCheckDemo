using UnityEngine;
using DG.Tweening;

public class FakeDragIntroLoop : MonoBehaviour
{
    public BoardController board;
    public float firstDelay = 0.8f;
    public float repeatInterval = 2.0f;

    public float liftScale = 1.15f;
    public float liftTime = 0.12f;
    public float dragTime = 0.35f;
    public float returnTime = 0.25f;

    bool _stopped;

    void Start()
    {
        ScheduleNext(firstDelay);
    }

    void Update()
    {
        if (_stopped) return;
        if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
            StopForever();
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
        DOTween.Kill(t); // 防叠加

        Vector3 startPos = t.position;
        Vector3 targetPos = board.CellToWorld(board.introTargetCell);

        // 只“拖向目标一小段”，起到示范作用，不真的放过去
        Vector3 toward = (targetPos - startPos).normalized * (board.cellSize * 2 * 0.8f);
        Vector3 endPos = startPos + toward;

        Sequence seq = DOTween.Sequence().SetTarget(t);
        seq.Append(t.DOScale(liftScale, liftTime).SetEase(Ease.OutQuad));
        seq.Append(t.DOMove(endPos, dragTime).SetEase(Ease.OutQuad));
        seq.Append(t.DOMove(startPos, returnTime).SetEase(Ease.InQuad));
        seq.Append(t.DOScale(1f, liftTime).SetEase(Ease.OutQuad));
    }

    void StopForever()
    {
        _stopped = true;
        DOTween.Kill(this);
        if (board != null && board.introHintModule != null)
            DOTween.Kill(board.introHintModule.transform);
    }
}