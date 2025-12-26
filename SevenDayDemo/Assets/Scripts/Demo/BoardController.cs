using System;
using System.Collections;
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
    public GameObject cellPrefab;
    public bool useIntroLayout = true;
    private bool tutorialDone = true;

    public TextMeshProUGUI _hpText;
    public TextMeshProUGUI _chainText;

    [Header("Goal / Boss")] public int bossMaxHp = 2000;
    private int _bossHp;

    // Turn resolve state
    private bool _resolvingTurn;
    public bool IsResolvingTurn => _resolvingTurn;

    private void Awake()
    {
        _grid = new DraggableModule[width, height];

        InitCell();
    }

    private void Start()
    {
        _bossHp = Mathf.Max(1, bossMaxHp);
        _hpText?.SetText(_bossHp.ToString());
        _chainText?.SetText(string.Empty);

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

    public void ResolveChainsFrom(Vector2Int placedCell)
    {
        if (_resolvingTurn) return;
        StartCoroutine(ResolveTurnRoutine(placedCell));
    }

    private IEnumerator ResolveTurnRoutine(Vector2Int placedCell)
    {
        _resolvingTurn = true;
        _chainText?.SetText(string.Empty);

        // Step 1: 仅放置点触发首爆（规则更清晰）
        if (TryCollectMatchAt(placedCell, out var cluster))
        {
            // 教学模式：首爆后再进入正常循环
            if (!tutorialDone) tutorialDone = true;

            int chainIndex = 0;

            while (cluster != null && cluster.Count >= 3)
            {
                chainIndex++;

                int baseDmg = ComputeDamage(cluster.Count);
                float chainMul = GetChainMultiplier(chainIndex);
                int finalDmg = Mathf.RoundToInt(baseDmg * chainMul);

                ApplyBossDamage(finalDmg);
                _chainText?.SetText(chainIndex >= 2 ? $"x{chainIndex}" : string.Empty);

                yield return ExplodeRoutine(cluster);

                // 爆炸后施压生成（更容易形成连锁）
                if (tutorialDone)
                {
                    ApplyPressure(true);
                    if (IsBoardFull())
                    {
                        GameOver();
                        _resolvingTurn = false;
                        yield break;
                    }
                }

                // Step 2: 连消从全盘找任意直线匹配（不做掉落也可成立）
                if (!TryFindBestAnyMatch(out cluster))
                    break;
            }

            // 回合结束胜负判定
            if (_bossHp <= 0)
            {
                Win();
                _resolvingTurn = false;
                yield break;
            }

            _resolvingTurn = false;
            yield break;
        }

        // 没有首爆：回合结束施压（更强，制造压力）
        if (tutorialDone)
        {
            ApplyPressure(false);
            if (IsBoardFull())
            {
                GameOver();
                _resolvingTurn = false;
                yield break;
            }
        }

        _resolvingTurn = false;
    }

    private float GetChainMultiplier(int chainIndex)
    {
        // 1:1.0, 2:1.5, 3:2.0, 4+:2.5...
        if (chainIndex <= 1) return 1f;
        return 1f + 0.5f * (chainIndex - 1);
    }

    private void ApplyBossDamage(int dmg)
    {
        if (dmg <= 0) return;
        _bossHp = Mathf.Max(0, _bossHp - dmg);
        _hpText?.SetText(_bossHp.ToString());
        Debug.Log($"Damage {dmg}, BossHP={_bossHp}");
    }

    private bool TryCollectMatchAt(Vector2Int cell, out List<Vector2Int> cluster)
    {
        cluster = null;

        if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height)
            return false;

        var start = _grid[cell.x, cell.y];
        if (start == null) return false;

        int type = start.TypeId;
        var c = CollectLineMatches(cell, type);
        if (c.Count >= 3)
        {
            cluster = c;
            return true;
        }

        return false;
    }

    private bool TryFindBestAnyMatch(out List<Vector2Int> best)
    {
        best = null;
        int bestCount = 0;

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var m = _grid[x, y];
            if (m == null) continue;

            var c = CollectLineMatches(new Vector2Int(x, y), m.TypeId);
            if (c.Count >= 3 && c.Count > bestCount)
            {
                bestCount = c.Count;
                best = c;
            }
        }

        return best != null;
    }

    // 直线消除
    private List<Vector2Int> CollectLineMatches(Vector2Int origin, int type)
    {
        // 横向连续
        var horiz = new List<Vector2Int>();
        horiz.Add(origin);

        // 向左
        for (int x = origin.x - 1; x >= 0; x--)
        {
            var m = _grid[x, origin.y];
            if (m == null || m.TypeId != type) break;
            horiz.Add(new Vector2Int(x, origin.y));
        }

        // 向右
        for (int x = origin.x + 1; x < width; x++)
        {
            var m = _grid[x, origin.y];
            if (m == null || m.TypeId != type) break;
            horiz.Add(new Vector2Int(x, origin.y));
        }

        // 纵向连续
        var vert = new List<Vector2Int>();
        vert.Add(origin);

        // 向下
        for (int y = origin.y - 1; y >= 0; y--)
        {
            var m = _grid[origin.x, y];
            if (m == null || m.TypeId != type) break;
            vert.Add(new Vector2Int(origin.x, y));
        }

        // 向上
        for (int y = origin.y + 1; y < height; y++)
        {
            var m = _grid[origin.x, y];
            if (m == null || m.TypeId != type) break;
            vert.Add(new Vector2Int(origin.x, y));
        }

        // 只有“同一直线连续 >=3”才算
        bool horizOk = horiz.Count >= 3;
        bool vertOk = vert.Count >= 3;

        if (!horizOk && !vertOk)
            return new List<Vector2Int>(0);

        // 允许交叉：横线 + 竖线并集
        var set = new HashSet<Vector2Int>();
        if (horizOk)
            for (int i = 0; i < horiz.Count; i++)
                set.Add(horiz[i]);
        if (vertOk)
            for (int i = 0; i < vert.Count; i++)
                set.Add(vert[i]);

        return new List<Vector2Int>(set);
    }

    //洪水填充
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

    private System.Collections.IEnumerator ExplodeRoutine(List<Vector2Int> cells)
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

        SimpleCameraShake.Instance?.Shake(0.15f, 0.25f);
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

    private void InitCell()
    {
        if (cellPrefab == null) return;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var go = Instantiate(cellPrefab, transform);
            Vector3 p = new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, 0);
            go.transform.position = p;
            go.transform.localScale = new Vector3(cellSize * 0.95f, cellSize * 0.95f, 0.01f);
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


    /// <summary>
    /// Pressure spawn used during chain resolution: prefer spawning a piece that immediately creates a line match (>=3).
    /// This is what makes "chain reactions" reliably visible in a demo, without affecting normal (non-exploded) turns.
    /// If no immediate-match candidate exists, fallback to a fully random spawn (allowing instant explode).
    /// </summary>
    private bool SpawnOnePressurePreferMatch()
    {
        // Collect all (cell,type) that would immediately explode if spawned now.
        var candidates = new List<(Vector2Int cell, int type)>();

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            if (_grid[x, y] != null) continue;
            var cell = new Vector2Int(x, y);

            for (int type = 0; type < 3; type++)
            {
                if (WouldExplodeIfSpawn(cell, type))
                    candidates.Add((cell, type));
            }
        }

        if (candidates.Count > 0)
        {
            var pick = candidates[Random.Range(0, candidates.Count)];
            var m = Instantiate(modulePrefab);
            m.Init(this, pick.type, pick.cell);

            var sr = m.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = pick.type == 0 ? Color.white : (pick.type == 1 ? Color.gray : Color.black);

            Debug.Log($"[PressurePreferMatch] Spawned immediate-match: type={pick.type}, cell={pick.cell}");
            return true;
        }

        // No immediate-match candidate: fallback to random spawn (still allowing instant explode).
        var ok = SpawnOneRandomInEmpty_AllowInstantExplode();
        Debug.Log($"[PressurePreferMatch] No immediate-match candidates, fallback random ok={ok}");
        return ok;
    }

    public void ApplyPressure(bool exploded)
    {
        int spawnCount = exploded ? 1 : 2;

        for (int i = 0; i < spawnCount; i++)
        {
            bool ok = exploded
                ? SpawnOnePressurePreferMatch()
                : SpawnOneRandomInEmpty_NoInstantExplode();

            if (!ok)
            {
                GameOver();
                return;
            }
        }

        if (exploded)
            Debug.Log("After exploded pressure spawn, try find any match...");
    }

    private void GameOver()
    {
        Debug.Log("GAME OVER: board full");
        // Demo 阶段最小实现：直接重开第一局或随机局
        ClearBoard();
        SpawnIntroLayout();
        //或者显示一个简单文本
    }

    void Win()
    {
        Debug.Log("WIN: boss defeated");
        // Demo 阶段最小实现：重开一局
        ClearBoard();
        _bossHp = Mathf.Max(1, bossMaxHp);
        _hpText?.SetText(_bossHp.ToString());
        _chainText?.SetText(string.Empty);

        if (useIntroLayout) SpawnIntroLayout();
        else SpawnRandomLayout();
    }

    private List<Vector2Int> CollectLineMatches_Virtual(Vector2Int origin, int type, Vector2Int virtualCell)
    {
        bool IsSame(int x, int y)
        {
            if (x == virtualCell.x && y == virtualCell.y) return true;
            var m = _grid[x, y];
            return m != null && m.TypeId == type;
        }

        var horiz = new List<Vector2Int> { origin };
        for (int x = origin.x - 1; x >= 0 && IsSame(x, origin.y); x--) horiz.Add(new Vector2Int(x, origin.y));
        for (int x = origin.x + 1; x < width && IsSame(x, origin.y); x++) horiz.Add(new Vector2Int(x, origin.y));

        var vert = new List<Vector2Int> { origin };
        for (int y = origin.y - 1; y >= 0 && IsSame(origin.x, y); y--) vert.Add(new Vector2Int(origin.x, y));
        for (int y = origin.y + 1; y < height && IsSame(origin.x, y); y++) vert.Add(new Vector2Int(origin.x, y));

        bool horizOk = horiz.Count >= 3;
        bool vertOk = vert.Count >= 3;
        if (!horizOk && !vertOk) return new List<Vector2Int>(0);

        var set = new HashSet<Vector2Int>();
        if (horizOk)
            for (int i = 0; i < horiz.Count; i++)
                set.Add(horiz[i]);
        if (vertOk)
            for (int i = 0; i < vert.Count; i++)
                set.Add(vert[i]);
        return new List<Vector2Int>(set);
    }

    private bool WouldExplodeIfSpawn(Vector2Int cell, int type)
    {
        if (_grid[cell.x, cell.y] != null) return true; // 不是空格就别生成
        var matched = CollectLineMatches_Virtual(cell, type, cell);
        return matched.Count >= 3;
    }

    public bool SpawnOneRandomInEmpty_AllowInstantExplode()
    {
        var empties = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (_grid[x, y] == null)
                empties.Add(new Vector2Int(x, y));

        if (empties.Count == 0) return false;

        var cell = empties[Random.Range(0, empties.Count)];
        int type = Random.Range(0, 3);

        var m = Instantiate(modulePrefab);
        m.Init(this, type, cell);

        var sr = m.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = type == 0 ? Color.white : (type == 1 ? Color.gray : Color.black);

        return true;
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


        Debug.Log($"Safe candidates: {candidates.Count}");

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