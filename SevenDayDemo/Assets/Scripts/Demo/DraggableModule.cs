using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class DraggableModule : MonoBehaviour
{
    [Header("Type")]
    public int TypeId = 0;

    [HideInInspector] public Vector2Int CurrentCell;

    private BoardController _board;
    private Vector3 _dragOffset;
    private Vector3 _startPos;
    private Vector2Int _startCell;
    private bool _dragging;

    public void Init(BoardController board, int typeId, Vector2Int spawnCell)
    {
        _board = board;
        TypeId = typeId;

        // 初始放置
        if (!_board.IsCellEmpty(spawnCell))
        {
            // Demo 阶段直接粗暴：找不到就丢到旁边
            transform.position = _board.CellToWorld(spawnCell) + Vector3.right * 3f;
            CurrentCell = new Vector2Int(-999, -999);
        }
        else
        {
            _board.PlaceModule(this, spawnCell);
        }
    }

    private void OnMouseDown()
    {
        FakeDragIntroLoop.Instance?.InterruptForUserDrag(transform);

        if (_board == null) return;

        // 回合结算中 / 炸弹瞄准模式下，禁止拖拽，避免状态交错
        if (_board.IsResolvingTurn) return;
        if (_board.IsBombMode) return;

        _dragging = true;

        _startPos = transform.position;
        _startCell = CurrentCell;

        // 避免拖拽开始时的缩放 Tween 与拖拽冲突
        transform.DOKill(complete: false);
        transform.localScale = Vector3.one;

        var mouseWorld = GetMouseWorld();
        _dragOffset = transform.position - mouseWorld;

        // 拖起时先从格子里“拿起”，避免自己占用导致放回失败
        if (IsInBoard(_startCell))
            _board.ClearCell(_startCell);
    }

    private void OnMouseDrag()
    {
        if (!_dragging) return;
        var mouseWorld = GetMouseWorld();
        transform.position = mouseWorld + _dragOffset;
    }

    private void OnMouseUp()
    {
        if (!_dragging) return;
        _dragging = false;

        // 计算目标格
        if (_board.TryWorldToCell(transform.position, out var cell))
        {
            // ✅ 规则：必须与起始格不同才算放置
            if (IsInBoard(_startCell) && cell == _startCell)
            {
                // 取消：回到起点 + 恢复占用，不结算回合
                transform.position = _startPos;
                _board.PlaceModule(this, _startCell);
                transform.DOPunchScale(Vector3.one * 0.08f, 0.12f, 6, 0.8f);
                return;
            }

            // 正常放置：目标格必须为空
            if (_board.IsCellEmpty(cell))
            {
                _board.PlaceModule(this, cell);
                _board.ResolveChainsFrom(cell); // 回合结算由 Board 处理
                return;
            }
        }

        // 放置失败：回弹并恢复占用
        transform.position = _startPos;
        if (IsInBoard(_startCell))
        {
            _board.PlaceModule(this, _startCell);
            transform.DOPunchScale(Vector3.one * 0.08f, 0.12f, 6, 0.8f);
        }
    }

    private void OnMouseEnter()
    {
        // 炸弹模式 / 结算中不要做 hover 动画，避免误导玩家
        if (_board != null && (_board.IsResolvingTurn || _board.IsBombMode)) return;
        if (_dragging) return;

        transform.DOKill(complete: false);
        transform.DOScale(1.08f, 0.1f);
    }

    private void OnMouseExit()
    {
        if (_board != null && (_board.IsResolvingTurn || _board.IsBombMode)) return;
        if (_dragging) return;

        transform.DOKill(complete: false);
        transform.DOScale(1f, 0.1f);
    }

    private static Vector3 GetMouseWorld()
    {
        var p = Input.mousePosition;
        p.z = 10f; // 2D 相机距离
        return Camera.main.ScreenToWorldPoint(p);
    }

    private bool IsInBoard(Vector2Int c)
    {
        if (_board == null) return false;
        return c.x >= 0 && c.y >= 0 && c.x < _board.width && c.y < _board.height;
    }
}
