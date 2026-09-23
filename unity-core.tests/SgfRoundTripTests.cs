using GoGame.Core;

namespace UnityCore.Tests;

/// <summary>
/// SGF 写入 → 读取往返测试，验证元信息 + 着法列表无丢失。
/// 不需要 KataGo 进程。
/// </summary>
public static class SgfRoundTripTests
{
    public static int Run()
    {
        int fails = 0;
        fails += TestBasicRoundTrip();
        fails += TestPassMoves();
        fails += TestKomiAndRule();
        return fails;
    }

    private static int TestBasicRoundTrip()
    {
        try
        {
            var board = new Board(9);
            board.RecordMove("bC3");
            board.RecordMove("wD3");
            board.RecordMove("bE5");

            var sgf = SgfWriter.Write(board, Stone.Black, "Alice", "KataGo");
            Assert.Contains(sgf, "SZ[9]", "应含 SZ[9]", ref _d0, ref _d1);
            Assert.Contains(sgf, "PB[Alice]", "应含 PB[Alice]", ref _d0, ref _d1);
            Assert.Contains(sgf, "PW[KataGo]", "应含 PW[KataGo]", ref _d0, ref _d1);
            Assert.Contains(sgf, "RU[Chinese]", "应含 RU[Chinese]", ref _d0, ref _d1);
            Assert.Contains(sgf, ";B[c3]", "应含 ;B[c3]", ref _d0, ref _d1);
            Assert.Contains(sgf, ";W[d3]", "应含 ;W[d3]", ref _d0, ref _d1);
            Assert.Contains(sgf, ";B[e5]", "应含 ;B[e5]", ref _d0, ref _d1);

            var (b2, moves, meta) = SgfReader.Parse(sgf);
            Assert.Equal(9, b2.Size, "解析后 Size=9", ref _d0, ref _d1);
            Assert.Equal(3, moves.Count, "3 着法", ref _d0, ref _d1);
            Assert.Equal("Alice", meta.BlackName, "PB=Alice", ref _d0, ref _d1);
            Assert.Equal("KataGo", meta.WhiteName, "PW=KataGo", ref _d0, ref _d1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestPassMoves()
    {
        try
        {
            var board = new Board(9);
            board.RecordMove("bC3");
            board.RecordMove("wpass");
            board.RecordMove("bpass");
            board.RecordMove("wD5");

            var sgf = SgfWriter.Write(board, Stone.Black);
            var (b2, moves, _) = SgfReader.Parse(sgf);
            Assert.Equal(4, moves.Count, "4 着法（含 2 pass）", ref _d0, ref _d1);
            Assert.Equal("wpass", moves[1], "第 2 手 pass", ref _d0, ref _d1);
            Assert.Equal("bpass", moves[2], "第 3 手 pass", ref _d0, ref _d1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static int TestKomiAndRule()
    {
        try
        {
            var board = new Board(19);
            board.RecordMove("bD4");

            var sgf = SgfWriter.Write(board, Stone.Black, komi: 6.5);
            Assert.Contains(sgf, "KM[6.5]", "贴目 6.5", ref _d0, ref _d1);

            var (_, _, meta) = SgfReader.Parse(sgf);
            Assert.Equal(6.5, meta.Komi, "解析 Komi=6.5", ref _d0, ref _d1);
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static string _d0 = "";
    private static string _d1 = "";
    private static int Fail(string msg) { Console.WriteLine($"  [FAIL] {msg}"); return 1; }
}
