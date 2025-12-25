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
        FakeDragIntroLoop.Instance?.CancelIfAnimating(transform);
        
        if (_board == null) return;
        _dragging = true;

        _startPos = transform.position;
        _startCell = CurrentCell;

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

        // 吸附：找到最近格
        if (_board.TryWorldToCell(transform.position, out var cell))
        {
            if (_board.IsCellEmpty(cell))
            {
                _board.PlaceModule(this, cell);
                _board.ResolveChainsFrom(cell);
                return;
            }
        }

        // 放置失败：冷处理，回到原位并恢复占用
        transform.position = _startPos;
        if (IsInBoard(_startCell))
            _board.PlaceModule(this, _startCell);
    }
    void OnMouseEnter()
    {
        transform.DOScale(1.08f, 0.1f);
    }

    void OnMouseExit()
    {
        transform.DOScale(1f, 0.1f);
    }

    private static Vector3 GetMouseWorld()
    {
        var p = Input.mousePosition;
        p.z = 10f; // 2D 相机距离
        return Camera.main.ScreenToWorldPoint(p);
    }

    private static bool IsInBoard(Vector2Int c) => c.x >= 0 && c.y >= 0;
}
