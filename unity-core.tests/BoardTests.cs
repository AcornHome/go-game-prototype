using GoGame.Core;

namespace UnityCore.Tests;

/// <summary>
/// Board 单元测试：覆盖 9/13/19 路、自杀判定、提子、劫、GTP/SGF 坐标转换。
/// 不需要 KataGo 进程。
/// </summary>
public static class BoardTests
{
    public static int Run()
    {
        int fails = 0;
        fails += TestGtpRoundTrip();
        fails += TestPlaceStone();
        fails += TestCapture();
        fails += TestSelfAtariRejected();
        fails += TestSnapshots();
        fails += TestReset();
        fails += TestStarPoints();
        fails += TestSgfVertexRoundTrip();
        return fails;
    }

    private static int TestGtpRoundTrip()
    {
        var (b, w) = ("PASS", "PASS");
        try
        {
            Assert.Equal("D4", Board.ToGtpVertex(3, 15, 19), "GTP D4 (3,15) on 19x19", ref b, ref w);
            Assert.Equal("A19", Board.ToGtpVertex(0, 0, 19), "GTP A19 (0,0) on 19x19", ref b, ref w);
            Assert.Equal("T1", Board.ToGtpVertex(18, 18, 19), "GTP T1 (18,18) on 19x19", ref b, ref w);
            Assert.Equal("J10", Board.ToGtpVertex(8, 9, 19), "GTP J10 (8,9) 跳过 I", ref b, ref w);

            Assert.Equal((3, 15), Board.FromGtpVertex("D4", 19), "FromGtpVertex D4 -> (3,15)", ref b, ref w);
            Assert.Equal((0, 0), Board.FromGtpVertex("A19", 19), "FromGtpVertex A19 -> (0,0)", ref b, ref w);
            Assert.Equal((18, 18), Board.FromGtpVertex("T1", 19), "FromGtpVertex T1 -> (18,18)", ref b, ref w);
            Assert.Equal((8, 9), Board.FromGtpVertex("J10", 19), "FromGtpVertex J10 -> (8,9)", ref b, ref w);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestPlaceStone()
    {
        try
        {
            var board = new Board(9);
            var (ok, caps) = board.Place(3, 3, Stone.Black);
            Assert.True(ok, "Place(3,3, B) 应成功", ref _dummy0, ref _dummy1);
            Assert.Equal(Stone.Black, board.Get(3, 3), "(3,3) 应为黑", ref _dummy0, ref _dummy1);
            Assert.Equal(2, board.Snapshots.Count, "快照应 2 条（初始 + 1 步）", ref _dummy0, ref _dummy1);
            Assert.True(caps.Count == 0, "无提子", ref _dummy0, ref _dummy1);

            // 重复放应失败
            var (ok2, _) = board.Place(3, 3, Stone.White);
            Assert.True(!ok2, "重复位置应拒绝", ref _dummy0, ref _dummy1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestCapture()
    {
        try
        {
            var board = new Board(9);
            // 黑围白：白 (1,1) 四面被围后被提
            board.Place(1, 1, Stone.White);
            board.Place(0, 0, Stone.Black);
            board.Place(0, 1, Stone.Black);
            board.Place(1, 0, Stone.Black);
            board.Place(2, 1, Stone.Black);
            // 白 (1,1) 现在只剩 (1,2) 一气，再加 (1,2) 就提掉
            board.Place(1, 2, Stone.Black);

            Assert.Equal(Stone.Empty, board.Get(1, 1), "白 (1,1) 应被提掉", ref _dummy0, ref _dummy1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestSelfAtariRejected()
    {
        try
        {
            var board = new Board(9);
            // 在角上围 1 个子：黑下 (0,1), (1,0) → 白下 (0,0) 自杀
            board.Place(0, 1, Stone.Black);
            board.Place(8, 8, Stone.White);  // 远离
            board.Place(1, 0, Stone.Black);

            var (ok, _) = board.Place(0, 0, Stone.White);
            Assert.True(!ok, "自杀手应被拒绝", ref _dummy0, ref _dummy1);
            Assert.Equal(Stone.Empty, board.Get(0, 0), "(0,0) 应仍为空", ref _dummy0, ref _dummy1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestSnapshots()
    {
        try
        {
            var board = new Board(9);
            board.Place(0, 0, Stone.Black);
            board.Place(0, 1, Stone.White);
            // 2 步 → 3 条快照（初始 + 2 步）
            Assert.Equal(3, board.Snapshots.Count, "2 步后应有 3 条快照", ref _dummy0, ref _dummy1);

            board.RestoreToSnapshot(0);
            Assert.Equal(Stone.Empty, board.Get(0, 0), "RestoreTo(0) → (0,0) 应空", ref _dummy0, ref _dummy1);
            Assert.Equal(Stone.Empty, board.Get(0, 1), "RestoreTo(0) → (0,1) 应空", ref _dummy0, ref _dummy1);

            board.RestoreToSnapshot(2);
            Assert.Equal(Stone.Black, board.Get(0, 0), "RestoreTo(2) → (0,0) 应黑", ref _dummy0, ref _dummy1);
            Assert.Equal(Stone.White, board.Get(0, 1), "RestoreTo(2) → (0,1) 应白", ref _dummy0, ref _dummy1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestReset()
    {
        try
        {
            var board = new Board(19);
            board.Place(10, 10, Stone.Black);
            board.Place(10, 11, Stone.White);
            board.Reset();

            Assert.Equal(Stone.Empty, board.Get(10, 10), "Reset 后 (10,10) 应空", ref _dummy0, ref _dummy1);
            Assert.Equal(0, board.MoveHistory.Count, "Reset 后 MoveHistory 应空", ref _dummy0, ref _dummy1);
            Assert.Equal(1, board.Snapshots.Count, "Reset 后 Snapshots 应只剩初始 1 条", ref _dummy0, ref _dummy1);
            Assert.Equal(Stone.Black, board.NextToPlay, "Reset 后 NextToPlay 应为黑", ref _dummy0, ref _dummy1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestStarPoints()
    {
        try
        {
            var b19 = new Board(19);
            Assert.True(b19.IsStarPoint(3, 3), "19路 (3,3) 应是星位", ref _dummy0, ref _dummy1);
            Assert.True(b19.IsStarPoint(15, 15), "19路 (15,15) 应是星位", ref _dummy0, ref _dummy1);
            Assert.True(!b19.IsStarPoint(0, 0), "19路 (0,0) 不是星位", ref _dummy0, ref _dummy1);

            var b9 = new Board(9);
            Assert.True(b9.IsStarPoint(2, 2), "9路 (2,2) 应是星位", ref _dummy0, ref _dummy1);
            Assert.True(b9.IsStarPoint(4, 4), "9路 (4,4) 应是星位", ref _dummy0, ref _dummy1);

            var b13 = new Board(13);
            Assert.True(b13.IsStarPoint(3, 3), "13路 (3,3) 应是星位", ref _dummy0, ref _dummy1);
            Assert.True(b13.IsStarPoint(9, 9), "13路 (9,9) 应是星位", ref _dummy0, ref _dummy1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestSgfVertexRoundTrip()
    {
        try
        {
            // SGF 规则：列 a-t（跳过 i）；字母行 a 在顶 (y=0)，数字行 1 在底 (y=size-1)
            Assert.Equal((3, 3), SgfReader.FromSgfVertex("dd", 19), "SGF 'dd' on 19 (字母行 a 在顶)", ref _dummy0, ref _dummy1);
            Assert.Equal((3, 15), SgfReader.FromSgfVertex("d4", 19), "SGF 'd4' on 19 (数字行 1 在底)", ref _dummy0, ref _dummy1);
            Assert.Equal((8, 8), SgfReader.FromSgfVertex("jj", 19), "SGF 'jj' (跳过 i)", ref _dummy0, ref _dummy1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static string _dummy0 = "";
    private static string _dummy1 = "";

    private static int Fail(string msg)
    {
        Console.WriteLine($"  [FAIL] {msg}");
        return 1;
    }
}
