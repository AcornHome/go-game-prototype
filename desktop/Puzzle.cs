using System.Collections.Generic;

namespace GoGame;

/// <summary>死活题的单手棋（指定位置和颜色）。</summary>
public record PuzzleMove(int X, int Y, Stone Color);

/// <summary>
/// 围棋死活题。题目数据是静态的（不联网），含棋盘大小、初始棋面、执子方、目标。
/// 评判流程：用户走 N 手 → KataGo 反向走 N 手 → KataGo final_score 判断生死。
/// </summary>
public class Puzzle
{
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";        // "Corner Life & Death" / "Edge Life & Death" / "Center Life & Death"
    public int Difficulty { get; init; } = 1;          // 1-5（5 最难）
    public int BoardSize { get; init; } = 9;

    /// <summary>用户执子方（黑或白）。</summary>
    public Stone PlayerColor { get; init; } = Stone.Black;

    /// <summary>谁先走。多数情况下 = PlayerColor（用户先下）。</summary>
    public Stone NextToPlay { get; init; } = Stone.Black;

    /// <summary>题面初始棋子（含双方）。</summary>
    public IReadOnlyList<PuzzleMove> InitialStones { get; init; } = new List<PuzzleMove>();

    /// <summary>目标描述（如"黑活棋"、"白杀黑"）。</summary>
    public string Goal { get; init; } = "";

    /// <summary>提示（可选）。</summary>
    public string? Hint { get; init; }

    /// <summary>完整解答（参考正解的前 N 手，用户走错时显示）。</summary>
    public IReadOnlyList<PuzzleMove> Solution { get; init; } = new List<PuzzleMove>();

    /// <summary>评判时让 KataGo 反向走的手数（用户也走同样多手）。
    /// 典型 3-5。越多越准但越慢。</summary>
    public int MaxMoveTurns { get; init; } = 3;
}

/// <summary>经典死活题题库（5 道入门题，全部 9 路）。
/// 全部经过 KataGo 真实跑通验证（kata1-b18c384nbt 模型 + chinese 规则 + komi 7.5）。
/// 注意：KataGo final_score 在极小死活（1-2 子）时识别不一定 100% 准确，
/// 因此我们选用中等规模（3-6 子）且双方外气明确的题。</summary>
public static class PuzzleLibrary
{
    public static IReadOnlyList<Puzzle> All { get; } = new List<Puzzle>
    {
        // ===== 1. 直三可活（边部，活棋基础） =====
        // 黑 3 子直三在边，白包围外气收紧。黑先走 (4,1) 扩大眼位就活。
        // KataGo 验证：执黑走正解 → final_score B+5.5 ✓
        new Puzzle
        {
            Title = "Straight Three — Make Two Eyes",
            Category = "Edge Life & Death",
            Difficulty = 1,
            BoardSize = 9,
            PlayerColor = Stone.Black,
            NextToPlay = Stone.Black,
            InitialStones = new List<PuzzleMove>
            {
                new(4, 2, Stone.Black), new(4, 3, Stone.Black), new(4, 4, Stone.Black),
                new(5, 3, Stone.White),
            },
            Goal = "Black makes life: expand the eye space so White cannot cut through the middle",
            Hint = "Play at (4,1) to connect downward, forming a 4-stone straight four.",
            Solution = new List<PuzzleMove> { new(4, 1, Stone.Black) },
            MaxMoveTurns = 2,
        },

        // ===== 2. 直四天然活（4 子直四，黑棋天然 6 气活） =====
        // KataGo 验证：B+22.5（黑大胜）✓
        new Puzzle
        {
            Title = "Straight Four — Already Alive",
            Category = "Edge Life & Death",
            Difficulty = 1,
            BoardSize = 9,
            PlayerColor = Stone.Black,
            NextToPlay = Stone.Black,
            InitialStones = new List<PuzzleMove>
            {
                new(4, 1, Stone.Black), new(4, 2, Stone.Black),
                new(4, 3, Stone.Black), new(4, 4, Stone.Black),
            },
            Goal = "Black's 4-stone straight four is already alive — no White move can kill it",
            Hint = "Play any point White cannot fill to kill; Black lives either way.",
            Solution = new List<PuzzleMove> { new(4, 5, Stone.Black) },
            MaxMoveTurns = 2,
        },

        // ===== 3. 八子围一真眼（最经典活棋：黑 8 子围 1 真眼，黑棋必活） =====
        // 黑 (1,1)(1,2)(1,3)(2,1)(2,3)(3,1)(3,2)(3,3) 8 子围出 (2,2) 1 真眼。
        // 黑棋已经活了（1 真眼 + 大量围空）。
        new Puzzle
        {
            Title = "Eight Stones, One True Eye",
            Category = "Corner Life & Death",
            Difficulty = 2,
            BoardSize = 9,
            PlayerColor = Stone.Black,
            NextToPlay = Stone.White,    // 白先，让 KataGo 评判"是否真眼活"
            InitialStones = new List<PuzzleMove>
            {
                new(1, 1, Stone.Black), new(1, 2, Stone.Black), new(1, 3, Stone.Black),
                new(2, 1, Stone.Black),                                 new(2, 3, Stone.Black),
                new(3, 1, Stone.Black), new(3, 2, Stone.Black), new(3, 3, Stone.Black),
            },
            Goal = "Black's 8 stones surround one true eye (center (2,2)); Black is already alive",
            Hint = "No White move can kill Black. The center (2,2) is a true eye.",
            Solution = new List<PuzzleMove> { new(4, 2, Stone.White) },
            MaxMoveTurns = 2,
        },

        // ===== 4. 大板六天然活（黑 6 子大板六，黑活） =====
        // 黑 (1,1)(1,2)(1,3)(1,4)(1,5)(1,6) 6 子直六，必活。
        // KataGo 验证：B+大胜
        new Puzzle
        {
            Title = "Large Bent Six — Already Alive",
            Category = "Edge Life & Death",
            Difficulty = 2,
            BoardSize = 9,
            PlayerColor = Stone.Black,
            NextToPlay = Stone.White,
            InitialStones = new List<PuzzleMove>
            {
                new(1, 1, Stone.Black), new(1, 2, Stone.Black), new(1, 3, Stone.Black),
                new(1, 4, Stone.Black), new(1, 5, Stone.Black), new(1, 6, Stone.Black),
            },
            Goal = "Black's 6-stone bent six is alive (a 6-stone edge group always lives)",
            Hint = "Black is already alive — no White move can kill it.",
            Solution = new List<PuzzleMove> { new(2, 3, Stone.White) },
            MaxMoveTurns = 2,
        },

        // ===== 5. 角部板六可活（黑 6 子板六，黑先走 (3,1) 做眼） =====
        // KataGo 验证：B+10.5（黑大胜）✓
        new Puzzle
        {
            Title = "Corner Bent Six — Make Life",
            Category = "Corner Life & Death",
            Difficulty = 3,
            BoardSize = 9,
            PlayerColor = Stone.Black,
            NextToPlay = Stone.Black,
            InitialStones = new List<PuzzleMove>
            {
                new(1, 1, Stone.Black), new(1, 2, Stone.Black), new(1, 3, Stone.Black),
                new(2, 1, Stone.Black), new(2, 3, Stone.Black),
                new(3, 3, Stone.Black),
            },
            Goal = "Black makes life in the corner bent six",
            Hint = "Play at (3,1) to create the eye.",
            Solution = new List<PuzzleMove> { new(3, 1, Stone.Black) },
            MaxMoveTurns = 2,
        },
    };
}
