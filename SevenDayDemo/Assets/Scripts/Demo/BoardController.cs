using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

public sealed class BoardController : MonoBehaviour
{
    [Header("Board")] public int width = 5;
    public int height = 5;
    public float cellSize = 1.2f;
    public Vector2 origin = new Vector2(-2.4f, -2.4f); // 左下角世界坐标

    [Header("Explosion / Damage")] public int baseDamage = 100;
    public AnimationCurve damageCurve = AnimationCurve.Linear(3, 1, 12, 6); // count -> multiplier
    public float explodeDelay = 0.05f;

    private DraggableModule[,] _grid;

    private static readonly Vector2Int[] Neigh4 =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
    };

    public DraggableModule introHintModule;
    public Vector2Int introTargetCell = new Vector2Int(2, 2); // 缺口
    public DraggableModule modulePrefab;
    public bool useIntroLayout = true;
    private bool tutorialDone = true;

    public TextMeshProUGUI _damageText;
    public Action<bool> OnTurnResolved; // 参数：是否爆发
    private int _score;

    private void Awake()
    {
        _grid = new DraggableModule[width, height];
    }
    private void Start()
    {
        _score = 0;
        _damageText?.SetText(_score.ToString());
        OnTurnResolved += exploded =>
        {
            // 教学关结束前，你可以先不施压，避免干扰首爆
            if (!tutorialDone)
            {
                if (exploded) tutorialDone = true; // 首次爆发后进入正常循环
                return;
            }

            // ✅ 结算点1：回合结束就检查满盘
            if (IsBoardFull())
            {
                GameOver();
                return;
            }

            ApplyPressure(exploded);

            // ✅ 结算点2：施压生成后再检查一次
            if (IsBoardFull())
            {
                GameOver();
                return;
            }
        };

        if (useIntroLayout) SpawnIntroLayout();
        else SpawnRandomLayout(); // 你原来的随机生成
    }
    //暂时不用
    private void SpawnRandomLayout()
    {
        List<Vector2Int> allSpawns = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            allSpawns.Add(new Vector2Int(x, y));

        // Fisher–Yates shuffle
        for (int i = allSpawns.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (allSpawns[i], allSpawns[j]) = (allSpawns[j], allSpawns[i]);
        }

        // Demo：丢 10 个模块，类型 0/1/2
        for (int i = 0; i < 10; i++)
        {
            var m = Instantiate(modulePrefab);
            int type = i % 3;
            var spawn = allSpawns[i];
            m.Init(this, type, spawn);
            // 颜色区分（也可以放 ModuleView）
            var sr = m.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = type == 0 ? Color.white : (type == 1 ? Color.gray : Color.black);
        }
    }

    public Vector3 CellToWorld(Vector2Int c)
    {
        return new Vector3(origin.x + c.x * cellSize, origin.y + c.y * cellSize, 0);
    }

    public bool TryWorldToCell(Vector3 world, out Vector2Int cell)
    {
        float fx = (world.x - origin.x) / cellSize;
        float fy = (world.y - origin.y) / cellSize;
        int x = Mathf.RoundToInt(fx);
        int y = Mathf.RoundToInt(fy);

        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            cell = default;
            return false;
        }

        cell = new Vector2Int(x, y);
        return true;
    }

    public bool IsCellEmpty(Vector2Int c) => _grid[c.x, c.y] == null;

    public void PlaceModule(DraggableModule m, Vector2Int cell)
    {
        _grid[cell.x, cell.y] = m;
        m.CurrentCell = cell;
        m.transform.position = CellToWorld(cell);
    }

    public void ClearCell(Vector2Int cell)
    {
        _grid[cell.x, cell.y] = null;
    }

    public bool ResolveChainsFrom(Vector2Int startCell)
    {
        var start = _grid[startCell.x, startCell.y];
        if (start == null)
        {
            OnTurnResolved?.Invoke(false);
            return false;
        }

        int type = start.TypeId;
        var cluster = FloodFillSameType(startCell, type);

        if (cluster.Count >= 3)
        {
            int dmg = ComputeDamage(cluster.Count);
            StartCoroutine(ExplodeRoutine(cluster, dmg, true));
            return true;
        }

        OnTurnResolved?.Invoke(false);
        return false;
    }

    private List<Vector2Int> FloodFillSameType(Vector2Int start, int type)
    {
        var result = new List<Vector2Int>(16);
        var q = new Queue<Vector2Int>();
        var visited = new HashSet<Vector2Int>();

        q.Enqueue(start);
        visited.Add(start);

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            var m = _grid[c.x, c.y];
            if (m == null || m.TypeId != type) continue;

            result.Add(c);

            for (int i = 0; i < Neigh4.Length; i++)
            {
                var n = c + Neigh4[i];
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (visited.Add(n))
                    q.Enqueue(n);
            }
        }

        return result;
    }

    private int ComputeDamage(int count)
    {
        // 关键：不要线性。让 3、6、9 有“世界差异”
        float mult = damageCurve.Evaluate(count);
        return Mathf.RoundToInt(baseDamage * mult);
    }

    private System.Collections.IEnumerator ExplodeRoutine(List<Vector2Int> cells, int damage)
    {
        // “吵”：先轻微延迟+逐个清除（带节奏），再给伤害与抖动
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            var m = _grid[c.x, c.y];
            if (m != null)
            {
                ClearCell(c);
                Destroy(m.gameObject);
            }

            yield return new WaitForSeconds(explodeDelay);
        }

        // 如果你有 Cinemachine，可在这里触发 Impulse；没有也能先用简易抖动
        SimpleCameraShake.Instance?.Shake(0.15f, 0.25f);
    }

    private System.Collections.IEnumerator ExplodeRoutine(List<Vector2Int> cells, int damage, bool exploded)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            var m = _grid[c.x, c.y];
            if (m != null)
            {
                ClearCell(c);
                Destroy(m.gameObject);
            }

            yield return new WaitForSeconds(explodeDelay);
        }

        // TODO: 伤害文字/音效
        Debug.Log($"爆发! Count={cells.Count}, Damage={damage}");
        _score += damage;
        _damageText?.SetText(_score.ToString());
        SimpleCameraShake.Instance?.Shake(0.15f, 0.25f);

        // 协程末尾才算“回合结束”
        OnTurnResolved?.Invoke(exploded);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.15f);

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            Vector3 p = new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, 0);
            Gizmos.DrawWireCube(p, new Vector3(cellSize * 0.95f, cellSize * 0.95f, 0.01f));
        }
    }

    private void Reset()
    {
        // 让棋盘中心在 (0,0)
        origin = new Vector2(
            -((width - 1) * cellSize) * 0.5f,
            -((height - 1) * cellSize) * 0.5f
        );
    }

    private void ClearBoard()
    {
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var m = _grid[x, y];
            if (m != null) Destroy(m.gameObject);
            _grid[x, y] = null;
        }
    }

    private void SpawnIntroLayout()
    {
        ClearBoard();

        int[,] map = new int[5, 5]
        {
            // x: 0  1  2  3  4
            { -1, -1, -1, -1, -1 }, // y=0
            { -1, 1, 0, 2, -1 }, // y=1
            { 0, 0, -1, 0, -1 }, // y=2
            { -1, 1, 0, 2, -1 }, // y=3
            { -1, -1, -1, -1, -1 }, // y=4
        };

        for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
        {
            int type = map[y, x]; // 关键：map[y,x]
            if (type < 0) continue;

            var m = Instantiate(modulePrefab);
            m.Init(this, type, new Vector2Int(x, y));

            // 颜色区分（也可以放 ModuleView）
            var sr = m.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = type == 0 ? Color.white : (type == 1 ? Color.gray : Color.black);

            if (x == 0 && y == 2 && type == 0)
                introHintModule = m;
        }

        introTargetCell = new Vector2Int(2, 2);
    }

    public void ApplyPressure(bool exploded)
    {
        int spawnCount = exploded ? 1 : 2;

        for (int i = 0; i < spawnCount; i++)
        {
            if (!SpawnOneRandomInEmpty_NoInstantExplode())
            {
                // 关键决策：没有安全落子时怎么办？
                // Demo 推荐：直接 GameOver（规则清晰，玩家不会困惑）
                GameOver();
                return;
            }
        }
    }

    private void GameOver()
    {
        Debug.Log("GAME OVER: board full");
        // Demo 阶段最小实现：直接重开第一局或随机局
        // ClearBoard(); SpawnIntroLayout(); 或者显示一个简单文本
    }

    private bool WouldExplodeIfSpawn(Vector2Int cell, int type)
    {
        // cell 必须是空
        if (_grid[cell.x, cell.y] != null) return true;

        // BFS/FloodFill：把 cell 视为 type，其余格子照常
        var q = new Queue<Vector2Int>();
        var visited = new HashSet<Vector2Int>();

        q.Enqueue(cell);
        visited.Add(cell);

        int count = 0;

        while (q.Count > 0)
        {
            var c = q.Dequeue();

            // 判断该格子的“有效类型”
            int t;
            if (c == cell) t = type;
            else
            {
                var m = _grid[c.x, c.y];
                if (m == null) continue;
                t = m.TypeId;
            }

            if (t != type) continue;

            count++;
            if (count >= 3) return true; // 一旦达到 3，立刻判定会爆发

            for (int i = 0; i < Neigh4.Length; i++)
            {
                var n = c + Neigh4[i];
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (visited.Add(n)) q.Enqueue(n);
            }
        }

        return false;
    }

    public bool SpawnOneRandomInEmpty_NoInstantExplode()
    {
        // 收集所有候选（cell,type）
        var candidates = new List<(Vector2Int cell, int type)>();

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            if (_grid[x, y] != null) continue;

            var cell = new Vector2Int(x, y);
            for (int type = 0; type < 3; type++)
            {
                if (!WouldExplodeIfSpawn(cell, type))
                    candidates.Add((cell, type));
            }
        }

        if (candidates.Count == 0)
            return false; // 没有任何“安全落子”，交给上层决定：放宽规则 or GameOver

        var pick = candidates[Random.Range(0, candidates.Count)];
        var m = Instantiate(modulePrefab);
        m.Init(this, pick.type, pick.cell);
        
        // 颜色区分（也可以放 ModuleView）
        var sr = m.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = pick.type == 0 ? Color.white : (pick.type == 1 ? Color.gray : Color.black);
        
        return true;
    }
    public bool IsBoardFull()
    {
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (_grid[x, y] == null)
                return false;

        return true;
    }
}